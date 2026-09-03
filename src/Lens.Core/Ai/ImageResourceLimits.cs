namespace Lens.Core.Ai;

/// <summary>
/// [Hard limit kaldirildi - kesin product karari] Eskiden burada 50MB dosya
/// boyutu / 50MP cozunurluk ustundeki gorseller REDDEDILIYORDU
/// (ImageTooLargeException). Gecerli bir fabrika gorseli artik SADECE buyuk
/// oldugu icin reddedilmez.
///
/// Bu sinif artik exception FIRLATMAZ - yalnizca "bu gorsel ekonomik
/// (downsampled) decode'u hak edecek kadar buyuk mu?" sorusuna ucuz
/// (header-only, Image.Identify - tam piksel verisi okumadan) bir yanit
/// veren bir yardimci. Kullanim yerleri: ImagePreprocessor (indexing/query
/// embed) ve Lens.Desktop MainWindow (buyuk onizleme/zoom) - ayni esik,
/// TEK yerden kontrol edilir.
/// </summary>
public static class ImageResourceLimits
{
    /// <summary>
    /// Bu piksel sayisinin USTUNDEKI gorseller ekonomik (decoder-level
    /// downsampled) decode kullanir; ALTINDAKI (eskiden zaten kabul edilen)
    /// gorseller ONCEKI ile BIREBIR AYNI (tam cozunurluk) decode yolunu
    /// kullanmaya devam eder - boylece normal katalog/telefon fotograflarinda
    /// hicbir regresyon riski yoktur. Deger, eski (kaldirilan) 50MP hard
    /// limitiyle ayni - yeni bir sabit rejection esigi EKLENMEDI, mevcut
    /// deger yalnizca amaci degistirilerek (reject -> decode-strategy hint)
    /// yeniden kullanildi.
    /// </summary>
    public const long LargeImagePixelHint = 50_000_000;

    /// <summary>Header-only okuma ile piksel sayisini dondurur; okunamazsa (bozuk/erisilemez dosya) 0 doner - cagiran taraf normal/tam decode yoluna duser ve gercek hatayi decode asamasinda alir.</summary>
    public static long TryGetPixelCount(string imagePath)
    {
        try
        {
            var info = SixLabors.ImageSharp.Image.Identify(imagePath);
            return info is null ? 0 : (long)info.Width * info.Height;
        }
        catch
        {
            return 0;
        }
    }
}
