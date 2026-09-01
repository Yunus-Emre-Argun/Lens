namespace Lens.Core.Indexing;

public sealed class IndexUpdateStats
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int Unchanged { get; set; }

    /// <summary>Okunamayan/embed edilemeyen dosyalar icin "dosya adi: hata mesaji" girdileri.</summary>
    public List<string> Errors { get; } = new();

    public int Total => Added + Updated + Unchanged;
}
