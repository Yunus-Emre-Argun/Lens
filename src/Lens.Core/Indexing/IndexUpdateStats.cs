namespace Lens.Core.Indexing;

public sealed class IndexUpdateStats
{
    public int TotalFilesScanned { get; set; }
    public int SupportedImagesSeen { get; set; }

    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Removed { get; set; }

    public int UnsupportedFormatCount { get; set; }

    /// <summary>Gorsel olmayan/taninmayan uzantili dosya sayisi (orn. .pdf, .zip, .txt). Bu dosyalar embed edilmez ama Issues'da da ayrica listelenir - sessizce atlanmaz.</summary>
    public int SkippedNonImageCount { get; set; }

    /// <summary>Sorunlu/atlanan tek tek dosyalar (SupportedImageButFailed, UnsupportedImageFormat, NonImageFile).</summary>
    public List<IndexFileIssue> Issues { get; } = new();

    /// <summary>
    /// Taramanin KENDISI baslamadan/tamamlanmadan basarisiz olduysa (orn. UNC
    /// yola hic ulasilamadi) buraya yazilir; bu durumda Entries mevcut/eski
    /// index'in DEGISTIRILMEMIS halidir - hicbir sey kaybedilmez veya bozulmaz.
    /// </summary>
    public string? ScanError { get; set; }

    /// <summary>
    /// [Profil-dogrulamali index] Mevcut index dosyasi bu turda BASTAN
    /// olusturulmak zorunda kaldiysa nedeni (orn. "model dosyası SHA-256",
    /// "ön işleme sürümü (... → ...)", "Kayıt içeriği geçersiz"); aksi halde
    /// null. Kullaniciya "neden her sey yeniden indeksleniyor?" sorusunun
    /// yanitini gostermek icin vardir - sessiz bir tam yeniden tarama
    /// aciklanamaz gorunur (bkz. docs/DECISIONS.md #95).
    /// </summary>
    public string? IndexResetReason { get; set; }

    public int FailedCount => Issues.Count(i => i.Kind == FileIssueKind.SupportedImageButFailed);

    public int Total => Added + Updated + Unchanged;
}
