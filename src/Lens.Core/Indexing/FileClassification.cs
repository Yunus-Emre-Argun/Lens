namespace Lens.Core.Indexing;

public enum FileClassification
{
    /// <summary>Desteklenen bir gorsel uzantisi - embed pipeline'ina girer.</summary>
    SupportedImage,

    /// <summary>Bilinen bir gorsel formati ama Lens su an decode etmiyor.</summary>
    UnsupportedImageFormat,

    /// <summary>Gorsel degil (veya taninmayan uzanti) - sessizce atlanir, hata sayilmaz.</summary>
    NonImage,
}

/// <summary>
/// Uzanti bazli, genel dosya siniflandirmasi. Ozel-case (orn. ".lens_index.json
/// ise atla") YOK - herhangi bir gorsel-olmayan dosya dogal olarak NonImage'a
/// duser. Bilinen ama henuz desteklenmeyen gorsel formatlari (tif/bmp/webp/gif)
/// ayri siniflandirilir ki ileride destek eklemek kolay olsun ve kullaniciya
/// "bu bir gorsel ama acamiyorum" ile "bu zaten gorsel degil" ayirt edilebilsin.
/// </summary>
public static class FileClassifier
{
    public static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png",
    };

    public static readonly HashSet<string> KnownUnsupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".bmp", ".webp", ".gif",
    };

    public static FileClassification Classify(string extension)
    {
        if (SupportedImageExtensions.Contains(extension))
        {
            return FileClassification.SupportedImage;
        }

        if (KnownUnsupportedImageExtensions.Contains(extension))
        {
            return FileClassification.UnsupportedImageFormat;
        }

        return FileClassification.NonImage;
    }
}
