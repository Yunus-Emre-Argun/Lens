namespace Lens.Core.Ai;

/// <summary>
/// Model bir vektor uretti ama vektor KULLANILAMAZ: yanlis boyut, NaN/Infinity
/// iceriyor veya normu sifir (yon bilgisi yok). Bu durumda SESSIZCE devam
/// edilmez - cagiran taraf (indeksleme) bunu tek dosyaya ait bir "issue"
/// olarak isler, sorgu tarafinda ise arama baslatilmaz.
/// </summary>
public sealed class InvalidEmbeddingException : Exception
{
    public InvalidEmbeddingException(string message) : base(message)
    {
    }
}
