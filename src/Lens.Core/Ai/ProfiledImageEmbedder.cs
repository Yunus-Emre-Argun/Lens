using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lens.Core.Ai;

/// <summary>
/// Secilen <see cref="SearchModelProfile"/> ile calisan, model-bagimsiz
/// embedder. Dort kombinasyonun (DINO/CLIP x renkli/gri) tamamini tek bir
/// kod yolundan calistirir.
///
/// KRITIK - DINO RENKLI DAVRANISI KORUNUR: renkli modda uretilen tensor,
/// tek-modelli pilottaki <see cref="ImagePreprocessor.PreprocessToChwTensor(string, ImagePreprocessingProfile)"/>
/// ciktisiyla BIREBIR AYNIDIR (bit duzeyinde; bkz. AiProof Grup Q). Gri mod
/// yalnizca ek bir adim ekler ve renkli yolu HIC etkilemez.
/// </summary>
public sealed class ProfiledImageEmbedder : IImageEmbedder
{
    private readonly SearchModelProfile _model;
    private readonly InferenceSession _session;

    /// <inheritdoc />
    public EmbeddingProfile Profile { get; }

    /// <summary>Bu embedder'in calistirdigi model/renk kombinasyonu.</summary>
    public SearchModelProfile Model => _model;

    /// <summary>
    /// Modeli yukler ve profilini (dosyanin gercek SHA-256'si dahil) kurar.
    /// Hash hesabi diskten tek gecistir; UI thread'inde CAGRILMAMALIDIR.
    /// </summary>
    public ProfiledImageEmbedder(SearchModelProfile model, string onnxModelPath)
        : this(model, onnxModelPath, ModelFileHash.ComputeSha256(RequireFile(onnxModelPath)))
    {
    }

    /// <summary>Hash'i disaridan alan asiri yukleme - ayni dosyanin hash'i iki kez hesaplanmaz.</summary>
    public ProfiledImageEmbedder(SearchModelProfile model, string onnxModelPath, string modelSha256)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        RequireFile(onnxModelPath);
        Profile = model.CreateEmbeddingProfile(modelSha256);

        using var options = CreateSessionOptions();
        _session = new InferenceSession(onnxModelPath, options);
    }

    /// <summary>
    /// [Olculmus ayar] ONNX Runtime'in intra-op thread'leri varsayilan olarak
    /// her Run sonrasi bir sure MESGUL BEKLER (spinning). Indeksleme dongusu
    /// cikarim ile ImageSharp decode'unu SIRAYLA yaptigi icin bu iki is
    /// birbirini ac birakir. Olcum: 1155 ms -> 439 ms/gorsel (bkz.
    /// Lens.AiProof "ortbench"). Ayar yalnizca thread bekleme politikasidir;
    /// SAYISAL CIKTIYI DEGISTIRMEZ.
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

    /// <inheritdoc />
    public float[] Embed(string imagePath)
    {
        var chw = BuildTensor(imagePath);
        var size = _model.Preprocessing.CropSize;
        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, size, size });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(SearchModelProfile.OnnxInputName, tensor),
        };

        using var results = _session.Run(inputs);
        var output = results.FirstOrDefault(r => r.Name == _model.OnnxOutputName)
            ?? throw new InvalidEmbeddingException(
                $"ONNX modeli beklenen '{_model.OnnxOutputName}' çıktısını üretmedi "
                + $"(bulunanlar: {string.Join(", ", results.Select(r => r.Name))}).");

        return EmbeddingVector.L2NormalizeChecked(
            output.AsEnumerable<float>().ToArray(), _model.EmbeddingDimension);
    }

    /// <summary>
    /// Girdi tensorunu uretir.
    ///
    /// RENKLI modda mevcut, dogrulanmis <see cref="ImagePreprocessor"/> yolu
    /// AYNEN cagrilir - tek satir bile farkli hesaplanmaz. GRI modda yalnizca
    /// bir on adim eklenir: goruntu griye cevrilir (uc kanal korunur), sonra
    /// AYNI resize/crop/normalize adimlari uygulanir.
    /// </summary>
    internal float[] BuildTensor(string imagePath)
    {
        if (_model.ColorMode == ImageColorMode.Color)
        {
            return ImagePreprocessor.PreprocessToChwTensor(imagePath, _model.Preprocessing);
        }

        return GrayscalePreprocessor.PreprocessToChwTensor(imagePath, _model.Preprocessing);
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();
}

/// <summary>
/// Gri tonlamali on isleme. Renkli yoldan TEK farki, resize/crop'tan hemen
/// once doygunlugun sifirlanmasidir; normalizasyon sabitleri ve geometri
/// AYNEN modelin kendi profilinden gelir.
///
/// Tek kanalli veri modelin bekledigi UC kanala esit olarak kopyalanir
/// (ImageSharp'in <c>Grayscale()</c> islemi R=G=B birakir), boylece model
/// girdisinin sekli degismez.
///
/// Bu profil, renkli profilden FARKLI bir on isleme kimligi tasir (bkz.
/// <see cref="SearchModelProfile.PreprocessingVersion"/>) - gri ve renkli
/// embedding'ler ayni index'te karisamaz.
/// </summary>
public static class GrayscalePreprocessor
{
    /// <param name="imagePath">Kaynak gorsel.</param>
    /// <param name="profile">Modelin resmi on isleme profili - geometri ve normalizasyon sabitleri BURADAN gelir.</param>
    public static float[] PreprocessToChwTensor(string imagePath, ImagePreprocessingProfile profile)
    {
        using var image = LoadForPreprocessing(imagePath, profile);

        // Griye cevirme, olcekleme/kirpma ONCESINDE yapilir: boylece gri
        // donusumu tam cozunurlukteki piksellerden hesaplanir ve
        // yeniden orneklemeden kaynaklanan fark olusmaz.
        image.Mutate(x => x.Grayscale());

        int cropSize = profile.CropSize;
        int shortest = Math.Min(image.Width, image.Height);
        float scale = (float)profile.ResizeShortestEdge / shortest;
        int resizedWidth = Math.Max(cropSize, (int)MathF.Round(image.Width * scale));
        int resizedHeight = Math.Max(cropSize, (int)MathF.Round(image.Height * scale));

        image.Mutate(x => x.Resize(resizedWidth, resizedHeight, KnownResamplers.Bicubic));

        int left = Math.Max(0, (image.Width - cropSize) / 2);
        int top = Math.Max(0, (image.Height - cropSize) / 2);
        image.Mutate(x => x.Crop(new Rectangle(left, top, cropSize, cropSize)));

        var mean = profile.Mean;
        var std = profile.Std;
        var tensor = new float[3 * cropSize * cropSize];
        int planeSize = cropSize * cropSize;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var px = row[x];
                    int idx = y * cropSize + x;

                    // Grayscale() sonrasi R=G=B'dir; yine de her kanal kendi
                    // mean/std'siyle normalize edilir - modelin bekledigi
                    // sozlesme budur.
                    tensor[idx] = (px.R / 255f - mean[0]) / std[0];
                    tensor[planeSize + idx] = (px.G / 255f - mean[1]) / std[1];
                    tensor[2 * planeSize + idx] = (px.B / 255f - mean[2]) / std[2];
                }
            }
        });

        return tensor;
    }

    /// <summary>Renkli yoldaki ile AYNI ekonomik decode mantigi (bkz. ImagePreprocessor.LoadForPreprocessing).</summary>
    private static Image<Rgb24> LoadForPreprocessing(string imagePath, ImagePreprocessingProfile profile)
    {
        var pixelCount = ImageResourceLimits.TryGetPixelCount(imagePath);
        if (pixelCount > ImageResourceLimits.LargeImagePixelHint)
        {
            int hint = profile.ResizeShortestEdge * 2;
            var decoderOptions = new DecoderOptions { TargetSize = new Size(hint, hint) };
            return Image.Load<Rgb24>(decoderOptions, imagePath);
        }

        return Image.Load<Rgb24>(imagePath);
    }
}
