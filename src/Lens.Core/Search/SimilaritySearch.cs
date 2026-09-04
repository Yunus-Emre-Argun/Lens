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
    /// <summary>"Maksimum 200 sonuç" sözleşmesi (Top-10 → 15 → 200, bkz. docs/DECISIONS.md).</summary>
    public const int MaxResults = 200;

    /// <summary>
    /// Float32 dot-product birikimi (512 terim) kaynakli kucuk hassasiyet
    /// farklari icin tolerans - orn. bir gorsel kendisiyle karsilastirildiginda
    /// matematiksel olarak 1.0 (%100) olmasi gerekirken 0.999999x
    /// hesaplanabilir. Threshold karsilastirmasi bu epsilon kadar esnek
    /// tutulur ki kullanicinin gordugu yuvarlanmis (%100) deger ile backend
    /// filtre karari celiskili gorunmesin.
    /// </summary>
    private const float ScoreEpsilon = 1e-4f;

    /// <summary>[Eski API - hala AiProof harness/benchmark tarafindan kullaniliyor] Threshold uygulamaz, yalnizca ilk k sonucu dondurur.</summary>
    public static List<SearchResult> TopK(float[] query, IReadOnlyList<ImageIndexEntry> entries, int k)
    {
        return entries
            .Select(e => new SearchResult(e.RelativePath, ClampScore(Dot(query, e.Embedding))))
            .OrderByDescending(r => r.Score)
            .Take(k)
            .ToList();
    }

    /// <summary>
    /// Cekirdek arama sozlesmesi: 1) tum skorlari hesapla, 2) score &gt;=
    /// threshold filtresini (inclusive) uygula, 3) azalan siraya koy, 4) en
    /// fazla <paramref name="maxResults"/> (varsayilan 200) sonuc al.
    /// </summary>
    /// <param name="minSimilarityPercent">0-100 araliginda, kullaniciya gosterilen "Minimum benzerlik (%)" degeri.</param>
    public static List<SearchResult> SearchWithThreshold(
        float[] query, IReadOnlyList<ImageIndexEntry> entries, double minSimilarityPercent, int maxResults = MaxResults)
    {
        var thresholdFraction = (float)(minSimilarityPercent / 100.0);
        var qualifying = new List<SearchResult>(entries.Count);

        foreach (var entry in entries)
        {
            var score = ClampScore(Dot(query, entry.Embedding));
            if (score >= thresholdFraction - ScoreEpsilon)
            {
                qualifying.Add(new SearchResult(entry.RelativePath, score));
            }
        }

        qualifying.Sort((a, b) => b.Score.CompareTo(a.Score));

        if (qualifying.Count > maxResults)
        {
            qualifying.RemoveRange(maxResults, qualifying.Count - maxResults);
        }

        return qualifying;
    }

    /// <summary>Cosine skorunu guvenli bicimde [-1,1] araligina sikistirir (float32 birikim hatasi araligin disina cikarabilir).</summary>
    private static float ClampScore(float score) => Math.Clamp(score, -1f, 1f);

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
