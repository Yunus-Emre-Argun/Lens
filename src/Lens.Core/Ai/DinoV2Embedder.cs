using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lens.Core.Ai;

/// <summary>
/// [PILOT] DINOv2 ViT-B/14 (facebook/dinov2-base) vision encoder'ini ONNX
/// Runtime uzerinden CPU'da calistirir. Cikti, son katmanin CLS token'idir
/// (projeksiyon HEAD'i YOKTUR - DINOv2 self-supervised bir encoder'dir) ve
/// burada L2-normalize edilir.
///
/// ONNX sozlesmesi: girdi `pixel_values` float32 [batch,3,224,224], cikti
/// `image_embeds` float32 [batch,768], opset 17, dinamik batch. Model dosyasi
/// `benchmark/export_dinov2_onnx.py` ile resmi agirliklardan uretilir -
/// calisma zamaninda internetten indirilmez, Python gerektirmez.
///
/// CLIP ile ayni sinifta DEGIL, ayri bir tip: on isleme profili (bkz.
/// <see cref="ImagePreprocessingProfile.DinoV2"/>), embedding boyutu ve
/// profil kimligi farklidir; ikisi asla karismamalidir.
/// </summary>
public sealed class DinoV2Embedder : IImageEmbedder
{
    private readonly InferenceSession _session;
    private readonly ImagePreprocessingProfile _preprocessing = ImagePreprocessingProfile.DinoV2;

    public EmbeddingProfile Profile { get; }

    /// <summary>
    /// Modeli yukler ve profilini (dosyanin gercek SHA-256'si dahil) kurar.
    /// Hash hesabi diskten tek gecistir; bu constructor UI thread'inde
    /// CAGRILMAMALIDIR (bkz. MainWindow.TryEnsureEmbedderAsync - Task.Run).
    /// </summary>
    public DinoV2Embedder(string onnxModelPath)
        : this(onnxModelPath, ModelFileHash.ComputeSha256(RequireFile(onnxModelPath)))
    {
    }

    /// <summary>
    /// Hash'i disaridan alan asiri yukleme: cagiran taraf ayni dosyanin
    /// hash'ini zaten hesaplamissa (orn. model yuklenmeden once index
    /// profilini dogrulamak icin) ayni maliyet IKI KEZ odenmez.
    /// </summary>
    public DinoV2Embedder(string onnxModelPath, string modelSha256)
    {
        RequireFile(onnxModelPath);
        Profile = DinoV2BaseProfile.CreateProfile(modelSha256);

        // SessionOptions native bir kaynaktir ve oturum olusturulurken
        // ayarlari KOPYALANIR - bu yuzden hemen birakilabilir (ORT .NET'in
        // onerdigi kalip).
        using var options = CreateSessionOptions();
        _session = new InferenceSession(onnxModelPath, options);
    }

    /// <summary>
    /// [Olculmus ayar] ONNX Runtime'in intra-op thread'leri varsayilan olarak
    /// her Run sonrasi bir sure MESGUL BEKLER (spinning). Lens'in indeksleme
    /// dongusu ise cikarim ile ImageSharp decode'unu SIRAYLA, ayni surecte
    /// yapar - bu iki is birbirini ac birakir ve gorsel basina maliyet
    /// belirgin sekilde artar.
    ///
    /// Bu makinede (20 cekirdek) olculen degerler, 24 gerçek gorselle gerçek
    /// indeksleme deseni uzerinde (bkz. Lens.AiProof "ortbench" tanilama modu):
    ///
    ///   varsayilan            : cikarim 1090 ms + decode 65 ms = 1155 ms/gorsel
    ///   allow_spinning=0      : cikarim  422 ms + decode 17 ms =  439 ms/gorsel
    ///
    /// 5.000 gorsellik katalogda bu ~96 dakika ile ~37 dakika arasindaki
    /// farktir. Ayar SAYISAL SONUCU DEGISTIRMEZ - yalnizca thread bekleme
    /// politikasidir; ayni tensor icin cikti bit-bit aynidir.
    ///
    /// IntraOpNumThreads BILEREK AYARLANMADI: sabit bir thread sayisi (orn. 4
    /// veya 8) bu makinede ek bir kazanc SAGLAMADI (35-42 dk bandinda kaldi) ve
    /// cekirdek sayisi bilinmeyen ofis bilgisayarlarinda kotu bir tahmin olma
    /// riski tasir - ORT'nin kendi secimi birakildi.
    ///
    /// KALAN RISK: saf cikarim (ImageSharp araya girmeden, ayni tensor) hala
    /// ~115 ms iken dongude ~422 ms olcuulmektedir; fark tamamen giderilmedi.
    /// Kok neden bu turda tam olarak izole edilemedi (spinning politikasinin
    /// otesinde thread havuzu/zamanlayici etkilesimi suphesi var) - acik bir
    /// sinirlama olarak raporlanmistir.
    /// </summary>
    private static SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions();
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        return options;
    }

    private static string RequireFile(string onnxModelPath)
    {
        if (!File.Exists(onnxModelPath))
        {
            throw new FileNotFoundException($"ONNX model dosyasi bulunamadi: {onnxModelPath}");
        }

        return onnxModelPath;
    }

    public float[] Embed(string imagePath)
    {
        var chw = ImagePreprocessor.PreprocessToChwTensor(imagePath, _preprocessing);
        var size = _preprocessing.CropSize;
        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, size, size });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(DinoV2BaseProfile.OnnxInputName, tensor),
        };

        using var results = _session.Run(inputs);
        var output = results.FirstOrDefault(r => r.Name == DinoV2BaseProfile.OnnxOutputName)
            ?? throw new InvalidEmbeddingException(
                $"ONNX modeli beklenen '{DinoV2BaseProfile.OnnxOutputName}' çıktısını üretmedi "
                + $"(bulunanlar: {string.Join(", ", results.Select(r => r.Name))}).");

        // Dogrulama + L2 normalizasyon TEK yerde: yanlis boyut, NaN/Infinity
        // ve sifir norm burada acik hataya donusur, sessizce gecmez.
        return EmbeddingVector.L2NormalizeChecked(
            output.AsEnumerable<float>().ToArray(), DinoV2BaseProfile.EmbeddingDimension);
    }

    public void Dispose() => _session.Dispose();
}
