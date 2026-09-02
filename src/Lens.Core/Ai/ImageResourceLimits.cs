namespace Lens.Core.Ai;

/// <summary>
/// [Reliability] Asiri buyuk dosya boyutlu veya asiri yuksek cozunurluklu bir
/// gorsel (yanlislikla urun klasorune konmus olcek-disi bir dosya, ya da
/// kullanicinin query olarak sectigi devasa bir fotograf) tam cozunurlukte
/// decode edilirse bellek tuketimi/donma/OutOfMemory riski olusturur.
///
/// Bu limitler TEK bir yerden kontrol edilir (indexing, query embed ve buyuk
/// onizleme/zoom - bkz. ImagePreprocessor.PreprocessToChwTensor ve
/// Lens.Desktop MainWindow.LoadQueryImage/TryOpenImagePreview cagrilari),
/// tam piksel verisi okunmadan ONCE (dosya boyutu + ImageSharp
/// Image.Identify header-only okuma). Yeni dependency eklenmedi.
///
/// Degerler (~50MB dosya, ~50MP piksel) normal katalog/telefon fotograflarini
/// (tipik olarak birkac MB, birkac MP) reddetmeyecek kadar genis, bir
/// cozunurluk/dosya "bombasini" durduracak kadar dar secildi.
/// </summary>
public static class ImageResourceLimits
{
    public const long MaxFileSizeBytes = 50L * 1024 * 1024;
    public const long MaxPixelCount = 50_000_000;

    /// <summary>Limit asilirsa <see cref="ImageTooLargeException"/> firlatir; format bozuksa karar vermez (gercek decode kendi hatasini uretir).</summary>
    public static void EnsureWithinLimits(string imagePath)
    {
        var fileInfo = new FileInfo(imagePath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            throw new ImageTooLargeException(
                $"Görsel boyutu desteklenen sınırı aşıyor ({fileInfo.Length / (1024 * 1024)} MB, sınır {MaxFileSizeBytes / (1024 * 1024)} MB).");
        }

        SixLabors.ImageSharp.ImageInfo? info;
        try
        {
            info = SixLabors.ImageSharp.Image.Identify(imagePath);
        }
        catch
        {
            return;
        }

        if (info is null)
        {
            return;
        }

        var pixelCount = (long)info.Width * info.Height;
        if (pixelCount > MaxPixelCount)
        {
            throw new ImageTooLargeException(
                $"Görsel boyutu desteklenen sınırı aşıyor ({info.Width}x{info.Height} ≈ {pixelCount / 1_000_000} MP, sınır {MaxPixelCount / 1_000_000} MP).");
        }
    }
}

public sealed class ImageTooLargeException : Exception
{
    public ImageTooLargeException(string message) : base(message)
    {
    }
}
