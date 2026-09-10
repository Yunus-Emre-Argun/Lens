using Lens.Core.Ai;
using Lens.Core.Indexing;

namespace Lens.Core.Search;

/// <summary>
/// [PILOT] Coklu gorunumlu (desen odakli) CLIP index'i icin arama.
///
/// <see cref="SimilaritySearch"/> tek vektorlu kayitlar icindir ve duz nokta
/// carpimi yapar. Burada her kayit <see cref="ClipPatternProfile.ViewCount"/>
/// adet 512'lik gorunum blogu tasir; skor iki asamada hesaplanir:
///
///   1. <b>Whitening:</b> katalogdaki TUM gorunum vektorlerinin ortalamasi
///      cikarilip yeniden normalize edilir. CLIP embedding'leri dar bir koni
///      icinde toplandigi icin her sey her seye "biraz benzer" cikar; ortak
///      yonu kaldirmak dogru eslesme ile rakip arasindaki ayrimi buyutur
///      (olcumde medyan pay 0,050 -> 0,244).
///
///   2. <b>Birlestirme:</b> global (tam goruntu, 0. gorunum) benzerligi ile
///      herhangi iki gorunum arasindaki EN IYI benzerligin esit agirlikli
///      toplami. Yalnizca "en iyi bolge"ye bakmak, tek bir kucuk ortak
///      motifin tum eslesmeyi tasimasina izin verirdi; yalnizca global'e
///      bakmak ise kismi/kaydirilmis deseni kacirirdi.
///
/// Ortalama BILEREK index dosyasinda SAKLANMAZ: katalog degistiginde
/// ortalama da degisir, saklanan bir deger sessizce eskir. Her aramada
/// yeniden hesaplanir - 5.000 kayitta ~15 milyon islem, sorgunun kendi
/// embedding maliyetinin yaninda ihmal edilebilir.
/// </summary>
public static class PatternSimilaritySearch
{
    /// <summary>Bkz. <see cref="SimilaritySearch.MaxResults"/> - ayni sozlesme, tek kaynak.</summary>
    public const int MaxResults = SimilaritySearch.MaxResults;

    /// <summary>Bkz. <see cref="SimilaritySearch"/> - ayni gerekce (float32 birikim hatasi).</summary>
    private const float ScoreEpsilon = 1e-4f;

    /// <summary>
    /// Cekirdek arama sozlesmesi <see cref="SimilaritySearch.SearchWithThreshold"/>
    /// ile AYNIDIR: skorlari hesapla, <c>score &gt;= threshold</c> (inclusive)
    /// filtresini uygula, azalan siraya koy, en fazla
    /// <paramref name="maxResults"/> sonuc dondur. Yalnizca skorun NASIL
    /// hesaplandigi farklidir.
    /// </summary>
    /// <param name="query">Sorgu gorselinin birlesik (gorunum bazinda normalize) vektoru.</param>
    /// <param name="entries">Index kayitlari - her birinin embedding'i ayni uzunlukta olmalidir.</param>
    /// <param name="minSimilarityPercent">0-100 araliginda kullanici esigi.</param>
    /// <param name="maxResults">1-<see cref="MaxResults"/>.</param>
    public static List<SearchResult> SearchWithThreshold(
        float[] query, IReadOnlyList<ImageIndexEntry> entries,
        double minSimilarityPercent, int maxResults = MaxResults)
    {
        if (maxResults < 1 || maxResults > MaxResults)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults,
                $"maxResults 1-{MaxResults} araliginda olmalidir.");
        }

        int viewDim = ClipPatternProfile.ViewDimension;
        int viewCount = ClipPatternProfile.ViewCount;
        int expected = viewDim * viewCount;

        if (query is null || query.Length != expected)
        {
            throw new InvalidEmbeddingException(
                $"Sorgu embedding boyutu {query?.Length ?? 0}, beklenen {expected} "
                + $"({viewCount} görünüm × {viewDim}). Index farklı bir profille oluşturulmuş olabilir.");
        }

        var qualifying = new List<SearchResult>(entries.Count);
        if (entries.Count == 0)
        {
            return qualifying;
        }

        var mean = ComputeViewMean(entries, viewDim, viewCount, expected);

        // Sorgunun gorunumleri bir kez beyazlatilir - her aday icin tekrar
        // hesaplamak gereksiz olurdu.
        var whitenedQuery = WhitenViews(query, mean, viewDim, viewCount);

        var thresholdFraction = (float)(minSimilarityPercent / 100.0);
        var candidateBuffer = new float[expected];

        foreach (var entry in entries)
        {
            EmbeddingVector.EnsureComparable(query, entry.Embedding, entry.RelativePath);
            WhitenViewsInto(entry.Embedding, mean, viewDim, viewCount, candidateBuffer);

            var score = ScoreViews(whitenedQuery, candidateBuffer, viewDim, viewCount);
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

    /// <summary>Katalogdaki tum gorunum vektorlerinin ortalamasi (whitening yonu).</summary>
    private static float[] ComputeViewMean(
        IReadOnlyList<ImageIndexEntry> entries, int viewDim, int viewCount, int expected)
    {
        var sum = new double[viewDim];
        long count = 0;

        foreach (var entry in entries)
        {
            var embedding = entry.Embedding;
            if (embedding is null || embedding.Length != expected)
            {
                // Bozuk kayit burada sessizce atlanir; asil dogrulama index
                // yuklemesinde yapilir (bkz. IndexEntryValidation) - burasi
                // yalnizca ortalamanin bozulmamasini garanti eder.
                continue;
            }

            for (int view = 0; view < viewCount; view++)
            {
                int offset = view * viewDim;
                for (int i = 0; i < viewDim; i++)
                {
                    sum[i] += embedding[offset + i];
                }
            }

            count += viewCount;
        }

        var mean = new float[viewDim];
        if (count == 0)
        {
            return mean;
        }

        for (int i = 0; i < viewDim; i++)
        {
            mean[i] = (float)(sum[i] / count);
        }

        return mean;
    }

    private static float[] WhitenViews(float[] combined, float[] mean, int viewDim, int viewCount)
    {
        var result = new float[combined.Length];
        WhitenViewsInto(combined, mean, viewDim, viewCount, result);
        return result;
    }

    /// <summary>Her gorunum blogundan ortalamayi cikarir ve blogu yeniden L2-normalize eder (tahsis yapmadan, verilen tampona).</summary>
    private static void WhitenViewsInto(
        float[] combined, float[] mean, int viewDim, int viewCount, float[] destination)
    {
        for (int view = 0; view < viewCount; view++)
        {
            int offset = view * viewDim;
            double sumSquares = 0;
            for (int i = 0; i < viewDim; i++)
            {
                var value = combined[offset + i] - mean[i];
                destination[offset + i] = value;
                sumSquares += (double)value * value;
            }

            var norm = Math.Sqrt(sumSquares);
            if (norm <= 1e-9)
            {
                // Ortalamaya birebir esit bir gorunum (pratikte gorulmez):
                // yon bilgisi kalmadi, ham degerler korunur ki skor NaN olmasin.
                Array.Copy(combined, offset, destination, offset, viewDim);
                continue;
            }

            for (int i = 0; i < viewDim; i++)
            {
                destination[offset + i] = (float)(destination[offset + i] / norm);
            }
        }
    }

    /// <summary>Global terim (0. gorunum) + en iyi gorunum cifti, esit agirlikli.</summary>
    private static float ScoreViews(float[] query, float[] candidate, int viewDim, int viewCount)
    {
        double best = double.MinValue;
        for (int q = 0; q < viewCount; q++)
        {
            int qOffset = q * viewDim;
            for (int c = 0; c < viewCount; c++)
            {
                var dot = Dot(query, qOffset, candidate, c * viewDim, viewDim);
                if (dot > best)
                {
                    best = dot;
                }
            }
        }

        var global = Dot(query, 0, candidate, 0, viewDim);
        var combined = ClipPatternProfile.GlobalWeight * global + (1 - ClipPatternProfile.GlobalWeight) * best;
        return Math.Clamp((float)combined, -1f, 1f);
    }

    private static double Dot(float[] a, int aOffset, float[] b, int bOffset, int length)
    {
        double sum = 0;
        for (int i = 0; i < length; i++)
        {
            sum += a[aOffset + i] * b[bOffset + i];
        }

        return sum;
    }
}
