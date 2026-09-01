namespace Lens.Core.Indexing;

/// <summary>
/// Bir urun gorseli icin kalici index kaydi: dosya kimligi (relative path +
/// boyut + LastWriteTimeUtc) + embedding. Degisiklik tespiti bu alanlarla
/// yapilir (bkz. docs/DECISIONS.md #23) - hash tabanli daha karmasik bir
/// yontem MVP icin gerekli gorulmedi.
/// </summary>
public sealed class ImageIndexEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public long LastWriteTimeUtcTicks { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
}
