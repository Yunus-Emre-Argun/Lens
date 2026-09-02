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
/// Uzanti bazli, genel dosya siniflandirmasi - herhangi bir gorsel-olmayan
/// dosya dogal olarak NonImage'a duser (artik kullaniciya gorunur bir "sorun"
/// olarak yuzeye cikar, bkz. ImageIndex.ClassifyDirectory). Bilinen ama henuz
/// desteklenmeyen gorsel formatlari (tif/bmp/webp/gif) ayri siniflandirilir ki
/// ileride destek eklemek kolay olsun ve kullaniciya "bu bir gorsel ama
/// acamiyorum" ile "bu zaten gorsel degil" ayirt edilebilsin.
///
/// Bunun DISINDA, dosya ADI bazli kucuk bir "bilinen zararsiz dosya" listesi
/// var (KnownHarmlessFileNames) - Lens'in kendi eski artefaktlari (orn.
/// .lens_index.json, Faz 3B'den kalma; Faz 4A'da index konumu degisti) ve
/// Windows'un otomatik olusturdugu dosyalar (Thumbs.db, desktop.ini). Bunlar
/// kullanicinin hicbir sekilde mudahale etmedigi, klasorde olmasi normal
/// dosyalardir - "sorun" listesine hic girmezler, tamamen sessiz kalirlar.
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

    /// <summary>Tam dosya adiyla eslenir (uzanti degil) - Lens'in kendi artefaktlari + Windows'un otomatik dosyalari.</summary>
    public static readonly HashSet<string> KnownHarmlessFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lens_index.json",
        "thumbs.db",
        "desktop.ini",
    };

    public static bool IsKnownHarmless(string fileName) => KnownHarmlessFileNames.Contains(fileName);

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
