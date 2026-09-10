using Lens.Core.Ai;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lens.AiProof;

/// <summary>
/// [DENEY] Bir gorselden CLIP'e verilecek FARKLI "gorunum"ler uretir.
///
/// Amac, CLIP agirliklarini DEGISTIRMEDEN yalnizca girdi tarafini
/// (renk / donus / kadraj / olcek) degistirerek desen eslesmesinin
/// iyilesip iyilesmedigini olcmektir. Uretilen tensor her zaman CLIP'in
/// kendi resmi on isleme sozlesmesiyle (kisa kenar 224 + 224 center crop +
/// OpenAI mean/std) bitirilir - DINOv2 sabitleri KULLANILMAZ.
///
/// Her gorunumun kararli bir kimligi (<see cref="ViewId"/>) vardir; embedding
/// onbellegi bu kimlige gore tutulur, boylece ayni gorunum farkli
/// stratejilerde tekrar hesaplanmaz.
/// </summary>
public static class ClipPatternViews
{
    /// <summary>Her iki modelin de girdi kenari 224'tur.</summary>
    public const int Size = 224;

    /// <summary>Bir gorunumun nasil uretilecegini tanimlar: once (istege bagli) donus, sonra kadraj, sonra (istege bagli) gri.</summary>
    /// <param name="ViewId">Onbellek anahtari - kararli olmali, degisirse onbellek gecersizlesir.</param>
    /// <param name="Rotation">0/90/180/270 derece.</param>
    /// <param name="Crop">Kadraj stratejisi.</param>
    /// <param name="CropIndex">Izgara kadrajlarinda hucre sirasi; digerlerinde 0.</param>
    /// <param name="Grayscale">true ise doygunluk sifirlanir (uc kanal korunur - CLIP 3 kanal bekler).</param>
    public sealed record ViewSpec(string ViewId, int Rotation, CropMode Crop, int CropIndex, bool Grayscale);

    public enum CropMode
    {
        /// <summary>Mevcut davranis: kisa kenar 224'e olceklenir, merkezden 224 kesilir (kenarlar KAYBOLUR).</summary>
        CenterCrop,

        /// <summary>Tum goruntu 224x224 icine sigdirilir, bosluk beyazla doldurulur - hicbir sey kaybolmaz, desen KUCULUR.</summary>
        LetterboxPad,

        /// <summary>Merkezin %50'si kesilip 224'e buyutulur - desen daha BUYUK gorunur.</summary>
        CenterZoom2x,

        /// <summary>3x3 izgara hucresi (CropIndex 0-8).</summary>
        Grid3x3,

        /// <summary>Merkez + 4 kose, %60 boyutunda ortusen bolgeler (CropIndex 0-4).</summary>
        Overlap5,
    }

    // ---- Hazir gorunum tanimlari -------------------------------------------------

    /// <summary>Bugunku uretim davranisi - baseline. Tensoru <see cref="ImagePreprocessor"/> ile BIREBIR ayni olmalidir (bkz. SelfCheck).</summary>
    public static readonly ViewSpec RgbCenter = new("rgb.center", 0, CropMode.CenterCrop, 0, false);

    public static readonly ViewSpec GrayCenter = new("gray.center", 0, CropMode.CenterCrop, 0, true);
    public static readonly ViewSpec RgbPad = new("rgb.pad", 0, CropMode.LetterboxPad, 0, false);
    public static readonly ViewSpec GrayPad = new("gray.pad", 0, CropMode.LetterboxPad, 0, true);
    public static readonly ViewSpec RgbZoom = new("rgb.zoom2", 0, CropMode.CenterZoom2x, 0, false);

    public static ViewSpec RgbRotation(int degrees) =>
        new($"rgb.rot{degrees}", degrees, CropMode.CenterCrop, 0, false);

    public static ViewSpec GrayRotation(int degrees) =>
        new($"gray.rot{degrees}", degrees, CropMode.CenterCrop, 0, true);

    public static ViewSpec Grid(int index, bool gray = false) =>
        new($"{(gray ? "gray" : "rgb")}.g9.{index}", 0, CropMode.Grid3x3, index, gray);

    public static ViewSpec Overlap(int index, bool gray = false) =>
        new($"{(gray ? "gray" : "rgb")}.o5.{index}", 0, CropMode.Overlap5, index, gray);

    // ---- Tensor uretimi ----------------------------------------------------------

    /// <summary>
    /// Verilen gorselden, verilen gorunumler icin CHW float32 tensorler uretir.
    /// Gorsel diskten YALNIZCA BIR KEZ okunur - N gorunum icin N kez decode
    /// etmek deney suresini gereksiz yere N katina cikarirdi.
    /// </summary>
    /// <param name="imagePath">Kaynak gorsel.</param>
    /// <param name="views">Uretilecek gorunumler.</param>
    /// <param name="profile">Modelin resmi on isleme sozlesmesi - CLIP ve DINOv2 sabitleri KARISTIRILMAZ.</param>
    public static Dictionary<string, float[]> BuildTensors(
        string imagePath, IReadOnlyList<ViewSpec> views, ImagePreprocessingProfile profile)
    {
        using var source = Image.Load<Rgb24>(imagePath);
        var result = new Dictionary<string, float[]>(views.Count, StringComparer.Ordinal);

        foreach (var view in views)
        {
            if (result.ContainsKey(view.ViewId))
            {
                continue;
            }

            using var prepared = Prepare(source, view, profile);
            result[view.ViewId] = ToChwTensor(prepared, profile);
        }

        return result;
    }

    /// <summary>Kaynak gorselden, verilen gorunume karsilik gelen 224x224 RGB kareyi uretir.</summary>
    private static Image<Rgb24> Prepare(Image<Rgb24> source, ViewSpec view, ImagePreprocessingProfile profile)
    {
        var work = source.Clone();
        try
        {
            if (view.Rotation != 0)
            {
                work.Mutate(x => x.Rotate(view.Rotation));
            }

            switch (view.Crop)
            {
                case CropMode.CenterCrop:
                    ResizeShortestEdgeThenCenterCrop(work, profile);
                    break;

                case CropMode.LetterboxPad:
                    // Uzun kenari 224'e getir, kalani beyazla doldur: kenarlardaki
                    // desen KAYBOLMAZ, ama desen olcegi kuculur.
                    work.Mutate(x => x
                        .Resize(new ResizeOptions { Size = new Size(Size, Size), Mode = ResizeMode.Pad })
                        .BackgroundColor(Color.White));
                    break;

                case CropMode.CenterZoom2x:
                    CropRegion(work, 0.5f, 0.5f, 0.5f, 0.5f);
                    ResizeShortestEdgeThenCenterCrop(work, profile);
                    break;

                case CropMode.Grid3x3:
                {
                    int col = view.CropIndex % 3;
                    int row = view.CropIndex / 3;
                    CropRegion(work, col / 3f, row / 3f, 1f / 3f, 1f / 3f);
                    ResizeShortestEdgeThenCenterCrop(work, profile);
                    break;
                }

                case CropMode.Overlap5:
                {
                    // 0 = merkez, 1-4 = koseler; her biri kenarin %60'i, yani
                    // komsu bolgelerle ORTUSUR (bir motif tam ortada bolunup
                    // hicbir bolgede butun kalmama riskini azaltir).
                    const float f = 0.6f;
                    var (x0, y0) = view.CropIndex switch
                    {
                        1 => (0f, 0f),
                        2 => (1f - f, 0f),
                        3 => (0f, 1f - f),
                        4 => (1f - f, 1f - f),
                        _ => ((1f - f) / 2f, (1f - f) / 2f),
                    };
                    CropRegion(work, x0, y0, f, f);
                    ResizeShortestEdgeThenCenterCrop(work, profile);
                    break;
                }
            }

            if (view.Grayscale)
            {
                // Doygunlugu sifirla - kanal sayisi 3 KALIR (CLIP tek kanalli
                // girdi kabul etmez); boylece renk bilgisi tasinmaz ama
                // modelin bekledigi tensor sekli korunur.
                work.Mutate(x => x.Grayscale());
            }

            return work;
        }
        catch
        {
            work.Dispose();
            throw;
        }
    }

    /// <summary>Goruntuyu, oransal (0-1) bir dikdortgene kirpar. En az 8 piksel birakir - asiri kucuk kirpma decode hatasina yol acmasin.</summary>
    private static void CropRegion(Image<Rgb24> image, float xFraction, float yFraction, float wFraction, float hFraction)
    {
        int x = (int)(image.Width * xFraction);
        int y = (int)(image.Height * yFraction);
        int w = Math.Max(8, (int)(image.Width * wFraction));
        int h = Math.Max(8, (int)(image.Height * hFraction));
        x = Math.Clamp(x, 0, Math.Max(0, image.Width - w));
        y = Math.Clamp(y, 0, Math.Max(0, image.Height - h));
        image.Mutate(c => c.Crop(new Rectangle(x, y, w, h)));
    }

    /// <summary>Modelin resmi adimi: kisa kenari profilin hedefine bicubic olcekle, merkezden profilin crop'unu kes (CLIP 224/224, DINOv2 256/224).</summary>
    private static void ResizeShortestEdgeThenCenterCrop(Image<Rgb24> image, ImagePreprocessingProfile profile)
    {
        int crop = profile.CropSize;
        int shortest = Math.Min(image.Width, image.Height);
        float scale = (float)profile.ResizeShortestEdge / shortest;
        int w = Math.Max(crop, (int)MathF.Round(image.Width * scale));
        int h = Math.Max(crop, (int)MathF.Round(image.Height * scale));
        image.Mutate(x => x.Resize(w, h, KnownResamplers.Bicubic));

        int left = Math.Max(0, (image.Width - crop) / 2);
        int top = Math.Max(0, (image.Height - crop) / 2);
        image.Mutate(x => x.Crop(new Rectangle(left, top, crop, crop)));
    }

    /// <summary>224x224 RGB kareyi, CLIP mean/std ile normalize edilmis CHW float32 tensore cevirir.</summary>
    private static float[] ToChwTensor(Image<Rgb24> image, ImagePreprocessingProfile profile)
    {
        var mean = profile.Mean;
        var std = profile.Std;
        var tensor = new float[3 * Size * Size];
        int plane = Size * Size;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var px = row[x];
                    int idx = y * Size + x;
                    tensor[idx] = (px.R / 255f - mean[0]) / std[0];
                    tensor[plane + idx] = (px.G / 255f - mean[1]) / std[1];
                    tensor[2 * plane + idx] = (px.B / 255f - mean[2]) / std[2];
                }
            }
        });

        return tensor;
    }

    /// <summary>
    /// [Dogruluk guvencesi] "rgb.center" gorunumu, uretimdeki
    /// <see cref="ImagePreprocessor"/> ciktisiyla BIREBIR ayni olmalidir -
    /// aksi halde baseline olcumu gercek uygulamayi temsil etmez ve tum
    /// karsilastirma anlamsizlasir.
    /// </summary>
    public static bool SelfCheckMatchesProduction(
        string imagePath, ImagePreprocessingProfile profile, out string detail)
    {
        var mine = BuildTensors(imagePath, new[] { RgbCenter }, profile)[RgbCenter.ViewId];
        var production = ImagePreprocessor.PreprocessToChwTensor(imagePath, profile);

        if (mine.Length != production.Length)
        {
            detail = $"uzunluk farkli: {mine.Length} vs {production.Length}";
            return false;
        }

        float worst = 0;
        for (int i = 0; i < mine.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(mine[i] - production[i]));
        }

        detail = $"en buyuk mutlak fark {worst:0.#######}";
        return worst == 0f;
    }
}
