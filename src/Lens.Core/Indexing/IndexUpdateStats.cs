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
    public int SkippedNonImageCount { get; set; }

    /// <summary>Sorunlu tek tek dosyalar (hem SupportedImageButFailed hem UnsupportedImageFormat burada).</summary>
    public List<IndexFileIssue> Issues { get; } = new();

    /// <summary>
    /// Taramanin KENDISI baslamadan/tamamlanmadan basarisiz olduysa (orn. UNC
    /// yola hic ulasilamadi) buraya yazilir; bu durumda Entries mevcut/eski
    /// index'in DEGISTIRILMEMIS halidir - hicbir sey kaybedilmez veya bozulmaz.
    /// </summary>
    public string? ScanError { get; set; }

    public int FailedCount => Issues.Count(i => i.Kind == FileIssueKind.SupportedImageButFailed);

    public int Total => Added + Updated + Unchanged;
}
