using SixLabors.ImageSharp;
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
        // [Reliability] Tam piksel verisi okunmadan (Image.Load) ONCE boyut/
        // cozunurluk kontrolu - hem indexing hem query embed bu tek yoldan
        // gecer (bkz. ImageResourceLimits).
        ImageResourceLimits.EnsureWithinLimits(imagePath);

        using var image = Image.Load<Rgb24>(imagePath);

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
}
