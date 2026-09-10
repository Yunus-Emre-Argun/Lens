namespace Lens.Core.Indexing;

/// <summary>
/// Bir index kaydinin diskten okundugunda GECERLI sayilip sayilmayacagi. Iki
/// store da (legacy CLIP ve profil-dogrulamali) ayni kurali kullanir - tek
/// fark beklenen embedding boyutudur.
/// </summary>
public static class IndexEntryValidation
{
    public static bool IsValid(ImageIndexEntry? entry, int expectedDimension)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.RelativePath) || entry.Embedding is null)
        {
            return false;
        }

        if (entry.Embedding.Length != expectedDimension)
        {
            return false;
        }

        foreach (var value in entry.Embedding)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }
        }

        return true;
    }
}
