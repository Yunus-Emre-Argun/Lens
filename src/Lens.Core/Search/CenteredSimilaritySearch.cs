using Lens.Core.Ai;
using Lens.Core.Indexing;

namespace Lens.Core.Search;

/// <summary>Merkezlemenin bir arama icin uygulanip uygulanamadigi.</summary>
public enum CenteringOutcome
{
    /// <summary>Merkezleme uygulanmadi (kullanici kapatmis).</summary>
    Disabled,

    /// <summary>Merkezleme uygulandi.</summary>
    Applied,

    /// <summary>Uygulanamadi - katalog cok kucuk veya vektorler dejenere. SESSIZCE ham skora DONULMEZ; cagiran taraf kullaniciya bildirir.</summary>
    NotApplicable,
}

/// <param name="Outcome">Merkezlemenin durumu.</param>
/// <param name="Results">Arama sonuclari.</param>
/// <param name="Reason">Uygulanamadiysa insan okunabilir neden.</param>
public sealed record CenteredSearchResult(
    CenteringOutcome Outcome, List<SearchResult> Results, string? Reason);

/// <summary>
/// "Desen odakli karsilastirma" = <b>embedding merkezleme ve yeniden
/// normallestirme</b>.
///
/// TEKNIK ADLANDIRMA: burada kovaryans donusumu YAPILMAZ; yalnizca katalog
/// ortalamasi cikarilip vektorler yeniden L2-normalize edilir. Bu, tam
/// whitening DEGILDIR ve oyle sunulmamalidir.
///
/// Neden ise yarar: embedding'ler dar bir koni icinde toplanma egilimindedir,
/// bu yuzden her sey her seye "biraz benzer" cikar. Katalogda ORTAK olan yonu
/// kaldirmak, dogru eslesme ile rakip arasindaki ayrimi buyutur.
///
/// HAM INDEX DEGISMEZ: donusum yalnizca arama aninda uygulanir, bu yuzden
/// secenegi acip kapatmak YENIDEN INDEKSLEME GEREKTIRMEZ.
/// </summary>
public static class CenteredSimilaritySearch
{
    /// <summary>Ortalamanin anlamli olmasi icin gereken en az kayit sayisi. Tek kayitta ortalama kaydin kendisidir ve merkezlenmis vektor sifira duser.</summary>
    public const int MinimumEntriesForCentering = 2;

    /// <summary>
    /// Merkezleme KAPALI iken mevcut <see cref="SimilaritySearch"/> yoluna
    /// AYNEN devreder - DINO renkli varsayilan davranisi bit duzeyinde
    /// korunur. ACIK iken vektorler merkezlenip yeniden normalize edilir.
    /// </summary>
    public static CenteredSearchResult SearchWithThreshold(
        float[] query,
        IReadOnlyList<ImageIndexEntry> entries,
        double minSimilarityPercent,
        int maxResults,
        bool applyCentering,
        EmbeddingMeanCache? meanCache = null)
    {
        if (!applyCentering)
        {
            return new CenteredSearchResult(
                CenteringOutcome.Disabled,
                SimilaritySearch.SearchWithThreshold(query, entries, minSimilarityPercent, maxResults),
                null);
        }

        if (entries.Count < MinimumEntriesForCentering)
        {
            return new CenteredSearchResult(CenteringOutcome.NotApplicable, new List<SearchResult>(),
                $"Ortak özellik arındırma için en az {MinimumEntriesForCentering} kayıt gerekiyor "
                + $"(katalogda {entries.Count} var).");
        }

        var mean = meanCache?.GetOrCompute(entries) ?? EmbeddingMeanCache.Compute(entries);
        if (mean is null)
        {
            return new CenteredSearchResult(CenteringOutcome.NotApplicable, new List<SearchResult>(),
                "Katalog ortalaması hesaplanamadı (kayıtların boyutları tutarsız olabilir).");
        }

        if (query.Length != mean.Length)
        {
            throw new InvalidEmbeddingException(
                $"Sorgu embedding boyutu {query.Length}, index {mean.Length}. "
                + "Index farklı bir model profiliyle oluşturulmuş olabilir.");
        }

        var centeredQuery = TryCenter(query, mean);
        if (centeredQuery is null)
        {
            // Sorgu tam olarak katalog ortalamasina esit - yon bilgisi kalmadi.
            // SESSIZCE ham skora donmek, kullaniciya farkli bir yontemin
            // sonucunu ayni etiketle gostermek olurdu.
            return new CenteredSearchResult(CenteringOutcome.NotApplicable, new List<SearchResult>(),
                "Sorgu görseli katalog ortalamasından ayırt edilemedi; ortak özellik arındırma uygulanamadı.");
        }

        var thresholdFraction = (float)(minSimilarityPercent / 100.0);
        var qualifying = new List<SearchResult>(entries.Count);
        var buffer = new float[mean.Length];
        int degenerate = 0;

        foreach (var entry in entries)
        {
            EmbeddingVector.EnsureComparable(query, entry.Embedding, entry.RelativePath);

            if (!TryCenterInto(entry.Embedding, mean, buffer))
            {
                // Bu kayit ortalamayla ayni - merkezlenmis hali yonsuz.
                // Ham vektorle KARISTIRMAK yerine bu kayit atlanir ve
                // sayilir; kullaniciya toplu olarak bildirilir.
                degenerate++;
                continue;
            }

            var score = Math.Clamp(Dot(centeredQuery, buffer), -1f, 1f);
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

        // TUM kayitlar dejenere ise merkezleme anlamsizdir (orn. katalogdaki
        // butun gorseller birbirinin ayni).
        if (degenerate == entries.Count)
        {
            return new CenteredSearchResult(CenteringOutcome.NotApplicable, new List<SearchResult>(),
                "Katalogdaki tüm görseller birbirinin aynı görünüyor; ortak özellik arındırma uygulanamadı.");
        }

        var reason = degenerate > 0
            ? $"{degenerate} kayıt katalog ortalamasından ayırt edilemediği için bu aramada atlandı."
            : null;

        return new CenteredSearchResult(CenteringOutcome.Applied, qualifying, reason);
    }

    /// <summary>Bkz. <see cref="SimilaritySearch"/> - ayni gerekce (float32 birikim hatasi).</summary>
    private const float ScoreEpsilon = 1e-4f;

    /// <summary>Ortalamanin cikarilip yeniden normalize edilebilecegi bir vektor mu? Degilse null.</summary>
    private static float[]? TryCenter(float[] vector, float[] mean)
    {
        var result = new float[vector.Length];
        return TryCenterInto(vector, mean, result) ? result : null;
    }

    /// <summary>Tahsis yapmadan, verilen tampona merkezleyip normalize eder. Norm dejenere ise false doner ve tampon KULLANILMAMALIDIR.</summary>
    private static bool TryCenterInto(float[] vector, float[] mean, float[] destination)
    {
        double sumSquares = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            var value = vector[i] - mean[i];
            destination[i] = value;
            sumSquares += (double)value * value;
        }

        var norm = Math.Sqrt(sumSquares);
        if (norm <= 1e-9 || double.IsNaN(norm) || double.IsInfinity(norm))
        {
            return false;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = (float)(destination[i] / norm);
        }

        return true;
    }

    private static float Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return (float)sum;
    }
}
