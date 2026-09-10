using Lens.Core.Ai;
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
    /// <summary>"Maksimum 999 sonuç" sözleşmesi (Top-10 → 15 → 200 → 300 → 999, bkz. docs/DECISIONS.md).</summary>
    public const int MaxResults = 999;

    /// <summary>
    /// Float32 dot-product birikimi (modele gore 512/768 terim) kaynakli kucuk hassasiyet
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
            .Select(e => new SearchResult(e.RelativePath, ScoreOf(query, e)))
            .OrderByDescending(r => r.Score)
            .Take(k)
            .ToList();
    }

    /// <summary>
    /// Cekirdek arama sozlesmesi: 1) tum skorlari hesapla, 2) score &gt;=
    /// threshold filtresini (inclusive) uygula, 3) azalan siraya koy, 4) en
    /// fazla <paramref name="maxResults"/> (varsayilan 999) sonuc al.
    /// </summary>
    /// <param name="minSimilarityPercent">0-100 araliginda, kullaniciya gosterilen "Minimum benzerlik (%)" degeri.</param>
    /// <param name="maxResults">
    /// [Kullanici tercihi - "En fazla sonuç"] 1-<see cref="MaxResults"/> (999)
    /// araliginda olmalidir. UI (bkz. Lens.Desktop MainWindow) bu degeri
    /// aramaya BASLAMADAN once zaten dogrulamis olmalidir - burasi ikinci
    /// (cekirdek katman) dogrulamadir, GECERSIZ bir deger SESSIZCE baska bir
    /// sayiya donusturulmez, ArgumentOutOfRangeException firlatilir.
    /// </param>
    public static List<SearchResult> SearchWithThreshold(
        float[] query, IReadOnlyList<ImageIndexEntry> entries, double minSimilarityPercent, int maxResults = MaxResults)
    {
        if (maxResults < 1 || maxResults > MaxResults)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults,
                $"maxResults 1-{MaxResults} araliginda olmalidir.");
        }

        var thresholdFraction = (float)(minSimilarityPercent / 100.0);
        var qualifying = new List<SearchResult>(entries.Count);

        foreach (var entry in entries)
        {
            var score = ScoreOf(query, entry);
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

    /// <summary>
    /// [Boyut guvenligi] Bir kaydin skorunu, boyut esitligini ONCE dogrulayarak
    /// hesaplar. Onceden <see cref="Dot"/> sorgunun uzunlugu boyunca donuyordu:
    /// kayit KISA ise IndexOutOfRange, UZUN ise fazla terimler sessizce yok
    /// sayilarak YANLIS ama makul gorunen bir skor uretiliyordu. Farkli
    /// modellerin vektorleri (orn. 768 boyutlu DINOv2 sorgusu ile 512 boyutlu
    /// eski CLIP kaydi) karsilastirilamaz - bu bir veri butunlugu hatasidir ve
    /// aciklayici bir <see cref="InvalidEmbeddingException"/> ile bildirilir
    /// (bkz. EmbeddingVector.EnsureComparable, docs/DECISIONS.md #95).
    ///
    /// Normal islemde index profil dogrulamasindan gectigi icin bu kontrol asla
    /// tetiklenmemelidir; ikinci bir savunma katmanidir. Maliyeti, 768 terimli
    /// dot product yaninda ihmal edilebilir bir uzunluk karsilastirmasidir.
    /// </summary>
    private static float ScoreOf(float[] query, ImageIndexEntry entry)
    {
        EmbeddingVector.EnsureComparable(query, entry.Embedding, entry.RelativePath);
        return ClampScore(Dot(query, entry.Embedding));
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
