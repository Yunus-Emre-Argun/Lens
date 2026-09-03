using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lens.Core.Ai;

/// <summary>
/// CLIP (openai/clip-vit-base-patch16) HF image processor davranisini yeniden
/// uretir: shortest-edge=224 bicubic resize, 224x224 center crop,
/// rescale (1/255), CLIP mean/std ile normalize. Degerler HF
/// CLIPImageProcessor'dan (preprocessor_config.json) alinmistir.
/// </summary>
public static class ImagePreprocessor
{
    public const int TargetSize = 224;

    private static readonly float[] Mean = { 0.48145466f, 0.4578275f, 0.40821073f };
    private static readonly float[] Std = { 0.26862954f, 0.26130258f, 0.27577711f };

    public static float[] PreprocessToChwTensor(string imagePath)
    {
        using var image = LoadForPreprocessing(imagePath);

        int shortest = Math.Min(image.Width, image.Height);
        float scale = (float)TargetSize / shortest;
        int resizedWidth = Math.Max(TargetSize, (int)MathF.Round(image.Width * scale));
        int resizedHeight = Math.Max(TargetSize, (int)MathF.Round(image.Height * scale));

        image.Mutate(x => x.Resize(resizedWidth, resizedHeight, KnownResamplers.Bicubic));

        int left = Math.Max(0, (image.Width - TargetSize) / 2);
        int top = Math.Max(0, (image.Height - TargetSize) / 2);
        image.Mutate(x => x.Crop(new Rectangle(left, top, TargetSize, TargetSize)));

        var tensor = new float[3 * TargetSize * TargetSize];
        int planeSize = TargetSize * TargetSize;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var px = row[x];
                    int idx = y * TargetSize + x;
                    tensor[idx] = (px.R / 255f - Mean[0]) / Std[0];
                    tensor[planeSize + idx] = (px.G / 255f - Mean[1]) / Std[1];
                    tensor[2 * planeSize + idx] = (px.B / 255f - Mean[2]) / Std[2];
                }
            }
        });

        return tensor;
    }

    /// <summary>
    /// [Hard limit kaldirildi] Buyuk/asiri yuksek cozunurluklu bir gorsel
    /// artik REDDEDILMEZ - ancak tam cozunurlukte belleye alinmasi
    /// bellek/performans riski olusturabilecegi icin, ImageResourceLimits
    /// esiginin USTUNDEKI dosyalar ImageSharp'in decoder-seviyesi
    /// downsampling'i (DecoderOptions.TargetSize) ile daha ekonomik decode
    /// edilir. Esigin ALTINDAKI gorseller (eskiden zaten kabul edilenler)
    /// ONCEKI davranisla BIREBIR AYNI (tam cozunurluk Image.Load) yolu
    /// kullanmaya devam eder - normal katalog gorsellerinde CLIP sonuclarinda
    /// regresyon riski yoktur (bkz. AiProof hardeningtest Grup C).
    ///
    /// TargetSize, sonraki adimda zaten shortest-edge=224'e indirilecegini
    /// bildigimiz icin bicubic resize/crop kalitesine pay birakmak amaciyla
    /// hedefin 2 katini (448x448) kullanir - decoder bu boyuta EN YAKIN
    /// ekonomik decode'u yapar, tam piksel sayisi kadar bellek harcanmaz.
    /// </summary>
    private static Image<Rgb24> LoadForPreprocessing(string imagePath)
    {
        var pixelCount = ImageResourceLimits.TryGetPixelCount(imagePath);
        if (pixelCount > ImageResourceLimits.LargeImagePixelHint)
        {
            var decoderOptions = new DecoderOptions
            {
                TargetSize = new Size(TargetSize * 2, TargetSize * 2),
            };
            return Image.Load<Rgb24>(decoderOptions, imagePath);
        }

        return Image.Load<Rgb24>(imagePath);
    }
}
