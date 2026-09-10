using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lens.Core.Ai;

/// <summary>
/// HF image processor davranisini yeniden uretir: kisa kenari bicubic ile
/// profilin hedefine getir, merkezden kare crop al, rescale (1/255), profilin
/// mean/std'i ile normalize et, CHW float32 tensor dondur.
///
/// [Profil mimarisi] Sabitler artik BU SINIFTA DEGIL, modele ait
/// <see cref="ImagePreprocessingProfile"/> icindedir - CLIP ve DINOv2
/// degerleri ayni yerde karisik durmaz. Profilsiz eski imza
/// (<see cref="PreprocessToChwTensor(string)"/>) CLIP profiline delege eder;
/// CLIP'te kisa kenar ve crop AYNI (224) oldugu icin uretilen tensor profil
/// mimarisinden ONCEKI ile BIREBIR AYNIDIR.
/// </summary>
public static class ImagePreprocessor
{
    /// <summary>[Geriye uyumluluk] CLIP'in girdi boyutu. Yeni kod profilin <see cref="ImagePreprocessingProfile.CropSize"/> alanini kullanmalidir.</summary>
    public const int TargetSize = 224;

    /// <summary>[Geriye uyumluluk] CLIP profiliyle on isleme - mevcut cagiranlar (ClipEmbedder, AiProof harness) icin davranis DEGISMEDI.</summary>
    public static float[] PreprocessToChwTensor(string imagePath) =>
        PreprocessToChwTensor(imagePath, ImagePreprocessingProfile.Clip);

    /// <summary>
    /// Verilen modelin on isleme profiline gore CHW duzeninde, float32 bir
    /// girdi tensoru uretir (uzunluk: 3 * CropSize * CropSize). Bu, asil
    /// (profil-farkindali) uygulamadir - profilsiz asiri yukleme buraya CLIP
    /// profiliyle delege eder.
    /// </summary>
    /// <param name="imagePath">Okunacak gorselin tam yolu.</param>
    /// <param name="profile">Kullanilacak modelin resmi on isleme sozlesmesi (bkz. <see cref="ImagePreprocessingProfile"/>).</param>
    public static float[] PreprocessToChwTensor(string imagePath, ImagePreprocessingProfile profile)
    {
        using var image = LoadForPreprocessing(imagePath, profile);

        int cropSize = profile.CropSize;

        // Kisa kenari profilin hedefine olcekle. Math.Max(cropSize, ...) alt
        // siniri, sonraki crop'un HER ZAMAN goruntunun icinde kalmasini
        // garanti eder (asiri dikdortgen goruntulerde yuvarlama sonrasi uzun
        // kenarin crop'tan kucuk kalmasi mumkun degildir).
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
                    tensor[idx] = (px.R / 255f - mean[0]) / std[0];
                    tensor[planeSize + idx] = (px.G / 255f - mean[1]) / std[1];
                    tensor[2 * planeSize + idx] = (px.B / 255f - mean[2]) / std[2];
                }
            }
        });

        return tensor;
    }

    /// <summary>
    /// [Hard limit kaldirildi] Buyuk/asiri yuksek cozunurluklu bir gorsel
    /// artik REDDEDILMEZ - ancak tam cozunurlukte bellege alinmasi
    /// bellek/performans riski olusturabilecegi icin, ImageResourceLimits
    /// esiginin USTUNDEKI dosyalar ImageSharp'in decoder-seviyesi
    /// downsampling'i (DecoderOptions.TargetSize) ile daha ekonomik decode
    /// edilir. Esigin ALTINDAKI gorseller (eskiden zaten kabul edilenler)
    /// ONCEKI davranisla BIREBIR AYNI (tam cozunurluk Image.Load) yolu
    /// kullanmaya devam eder - normal katalog gorsellerinde sonuclarda
    /// regresyon riski yoktur (bkz. AiProof hardeningtest Grup C).
    ///
    /// TargetSize, sonraki adimda zaten profilin kisa kenarina indirilecegini
    /// bildigimiz icin bicubic resize/crop kalitesine pay birakmak amaciyla
    /// hedefin 2 katini kullanir - decoder bu boyuta EN YAKIN ekonomik
    /// decode'u yapar, tam piksel sayisi kadar bellek harcanmaz. (CLIP
    /// profilinde bu deger 448'dir - profil mimarisinden ONCEKI ile ayni.)
    /// </summary>
    private static Image<Rgb24> LoadForPreprocessing(string imagePath, ImagePreprocessingProfile profile)
    {
        var pixelCount = ImageResourceLimits.TryGetPixelCount(imagePath);
        if (pixelCount > ImageResourceLimits.LargeImagePixelHint)
        {
            int hint = profile.ResizeShortestEdge * 2;
            var decoderOptions = new DecoderOptions
            {
                TargetSize = new Size(hint, hint),
            };
            return Image.Load<Rgb24>(decoderOptions, imagePath);
        }

        return Image.Load<Rgb24>(imagePath);
    }
}
