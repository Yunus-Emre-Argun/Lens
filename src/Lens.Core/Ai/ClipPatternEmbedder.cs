using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lens.Core.Ai;

/// <summary>
/// [PILOT] Desen odakli CLIP embedder'i: bir gorselden <see cref="ClipPatternProfile.ViewCount"/>
/// gorunum (1 global + 5 ortusen bolge) uretir, her birini CLIP ile
/// embed edip AYRI AYRI L2-normalize eder ve tek bir birlesik vektor olarak
/// dondurur.
///
/// Neden birlesik tek vektor? Mevcut index/kilit/atomik-yazma altyapisi
/// (bkz. <see cref="Lens.Core.Indexing.ImageIndex"/>) kayit basina TEK bir
/// <c>float[]</c> tasir. Gorunumleri ard arda ekleyerek o altyapiyi
/// KOPYALAMADAN yeniden kullaniyoruz; okurken
/// <see cref="Search.PatternSimilaritySearch"/> vektoru 512'lik bloklara
/// bolup gorunum bazinda karsilastirir.
///
/// ONEMLI: Normalizasyon GORUNUM BAZINDADIR. Birlesik 3072'lik vektorun
/// tamamini normalize etmek gorunumleri birbirine bulastirirdi ve
/// gorunum-bazli benzerlik anlamini kaybederdi.
///
/// CLIP agirliklari DEGISTIRILMEMISTIR - <see cref="ClipEmbedder"/> ile ayni
/// resmi model dosyasi kullanilir.
/// </summary>
public sealed class ClipPatternEmbedder : IImageEmbedder
{
    private static readonly ImagePreprocessingProfile Preprocessing = ImagePreprocessingProfile.Clip;

    private readonly InferenceSession _session;

    /// <inheritdoc />
    public EmbeddingProfile Profile { get; }

    /// <summary>
    /// Modeli yukler ve profilini (dosyanin gercek SHA-256'si dahil) kurar.
    /// Hash hesabi diskten tek gecistir; UI thread'inde CAGRILMAMALIDIR.
    /// </summary>
    public ClipPatternEmbedder(string onnxModelPath)
        : this(onnxModelPath, ModelFileHash.ComputeSha256(RequireFile(onnxModelPath)))
    {
    }

    /// <summary>Hash'i disaridan alan asiri yukleme - ayni dosyanin hash'i iki kez hesaplanmaz.</summary>
    public ClipPatternEmbedder(string onnxModelPath, string modelSha256)
    {
        RequireFile(onnxModelPath);
        Profile = ClipPatternProfile.CreateProfile(modelSha256);

        using var options = new SessionOptions();

        // Olculmus ayar (bkz. DinoV2Embedder): varsayilan spinning politikasi,
        // decode ile cikarimin siralandigi indeksleme dongusunu belirgin
        // sekilde yavaslatir. Bu embedder gorsel basina 6 cikarim yaptigi
        // icin etki daha da buyuktur.
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        _session = new InferenceSession(onnxModelPath, options);
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
        var combined = new float[ClipPatternProfile.EmbeddingDimension];

        // Gorsel diskten YALNIZCA BIR KEZ okunur - 6 gorunum icin 6 kez decode
        // etmek indeksleme suresini gereksiz yere katlardi.
        using var source = LoadSource(imagePath);

        for (int view = 0; view < ClipPatternProfile.ViewCount; view++)
        {
            using var prepared = PrepareView(source, view);
            var tensor = ToChwTensor(prepared);
            var raw = RunModel(tensor);

            // Gorunum BAZINDA dogrulama + normalizasyon: yanlis boyut,
            // NaN/Infinity ve sifir norm burada acik hataya donusur.
            var normalized = EmbeddingVector.L2NormalizeChecked(raw, ClipPatternProfile.ViewDimension);
            Array.Copy(normalized, 0, combined, view * ClipPatternProfile.ViewDimension, ClipPatternProfile.ViewDimension);
        }

        return combined;
    }

    private static Image<Rgb24> LoadSource(string imagePath)
    {
        // Buyuk gorseller icin ekonomik (decoder seviyesinde downsampled)
        // decode - mevcut davranisla ayni esik ve mantik (bkz.
        // ImagePreprocessor.LoadForPreprocessing). Bolgeler bu goruntuden
        // kesilecegi icin hedef, tek gorunum halinden biraz daha genis
        // tutulur: en kucuk bolge kenarin %60'i, dolayisiyla kaynak en az
        // 224/0.60 ~ 374 piksel olmali.
        var pixelCount = ImageResourceLimits.TryGetPixelCount(imagePath);
        if (pixelCount > ImageResourceLimits.LargeImagePixelHint)
        {
            int hint = (int)(Preprocessing.ResizeShortestEdge / ClipPatternProfile.RegionFraction) * 2;
            var decoderOptions = new SixLabors.ImageSharp.Formats.DecoderOptions
            {
                TargetSize = new Size(hint, hint),
            };
            return Image.Load<Rgb24>(decoderOptions, imagePath);
        }

        return Image.Load<Rgb24>(imagePath);
    }

    /// <summary>Gorunum 0 = tam goruntu (merkez crop); 1-5 = %60 boyutunda ortusen bolgeler (merkez, sol-ust, sag-ust, sol-alt, sag-alt).</summary>
    private static Image<Rgb24> PrepareView(Image<Rgb24> source, int view)
    {
        var work = source.Clone();
        try
        {
            if (view > 0)
            {
                const float f = ClipPatternProfile.RegionFraction;
                var (xFraction, yFraction) = view switch
                {
                    2 => (0f, 0f),
                    3 => (1f - f, 0f),
                    4 => (0f, 1f - f),
                    5 => (1f - f, 1f - f),
                    _ => ((1f - f) / 2f, (1f - f) / 2f),
                };

                CropRegion(work, xFraction, yFraction, f);
            }

            ResizeShortestEdgeThenCenterCrop(work);
            return work;
        }
        catch
        {
            work.Dispose();
            throw;
        }
    }

    /// <summary>Oransal bir kare bolgeye kirpar; en az 8 piksel birakir (asiri kucuk kirpma decode hatasi vermesin).</summary>
    private static void CropRegion(Image<Rgb24> image, float xFraction, float yFraction, float sizeFraction)
    {
        int w = Math.Max(8, (int)(image.Width * sizeFraction));
        int h = Math.Max(8, (int)(image.Height * sizeFraction));
        int x = Math.Clamp((int)(image.Width * xFraction), 0, Math.Max(0, image.Width - w));
        int y = Math.Clamp((int)(image.Height * yFraction), 0, Math.Max(0, image.Height - h));
        image.Mutate(c => c.Crop(new Rectangle(x, y, w, h)));
    }

    /// <summary>CLIP'in resmi adimi - <see cref="ImagePreprocessor"/> ile AYNI (kisa kenar 224 bicubic + 224 merkez crop).</summary>
    private static void ResizeShortestEdgeThenCenterCrop(Image<Rgb24> image)
    {
        int target = Preprocessing.CropSize;
        int shortest = Math.Min(image.Width, image.Height);
        float scale = (float)Preprocessing.ResizeShortestEdge / shortest;
        int w = Math.Max(target, (int)MathF.Round(image.Width * scale));
        int h = Math.Max(target, (int)MathF.Round(image.Height * scale));
        image.Mutate(x => x.Resize(w, h, KnownResamplers.Bicubic));

        int left = Math.Max(0, (image.Width - target) / 2);
        int top = Math.Max(0, (image.Height - target) / 2);
        image.Mutate(x => x.Crop(new Rectangle(left, top, target, target)));
    }

    /// <summary>CLIP mean/std ile normalize edilmis CHW float32 tensor.</summary>
    private static float[] ToChwTensor(Image<Rgb24> image)
    {
        int size = Preprocessing.CropSize;
        var mean = Preprocessing.Mean;
        var std = Preprocessing.Std;
        var tensor = new float[3 * size * size];
        int plane = size * size;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var px = row[x];
                    int idx = y * size + x;
                    tensor[idx] = (px.R / 255f - mean[0]) / std[0];
                    tensor[plane + idx] = (px.G / 255f - mean[1]) / std[1];
                    tensor[2 * plane + idx] = (px.B / 255f - mean[2]) / std[2];
                }
            }
        });

        return tensor;
    }

    private float[] RunModel(float[] chw)
    {
        int size = Preprocessing.CropSize;
        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, size, size });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(ClipPatternProfile.OnnxInputName, tensor),
        };

        using var results = _session.Run(inputs);
        var output = results.FirstOrDefault(r => r.Name == ClipPatternProfile.OnnxOutputName)
            ?? throw new InvalidEmbeddingException(
                $"ONNX modeli beklenen '{ClipPatternProfile.OnnxOutputName}' çıktısını üretmedi "
                + $"(bulunanlar: {string.Join(", ", results.Select(r => r.Name))}).");

        return output.AsEnumerable<float>().ToArray();
    }

    /// <inheritdoc />
    public void Dispose() => _session.Dispose();
}
