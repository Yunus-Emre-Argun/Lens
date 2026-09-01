using Lens.Core.Indexing;

namespace Lens.Core.Search;

public sealed record SearchResult(string RelativePath, float Score);

/// <summary>
/// Brute-force cosine similarity + Top-K. Embedding'ler L2-normalize kabul
/// edilir (ClipEmbedder.Embed bunu garanti eder), bu yuzden dot product =
/// cosine similarity. Vector DB kullanilmiyor (bkz. docs/DECISIONS.md #11, #23).
/// </summary>
public static class SimilaritySearch
{
    public static List<SearchResult> TopK(float[] query, IReadOnlyList<ImageIndexEntry> entries, int k)
    {
        return entries
            .Select(e => new SearchResult(e.RelativePath, Dot(query, e.Embedding)))
            .OrderByDescending(r => r.Score)
            .Take(k)
            .ToList();
    }

    private static float Dot(float[] a, float[] b)
    {
        float sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
