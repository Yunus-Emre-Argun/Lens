using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lens.Core.Ai;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using V = Lens.AiProof.ClipPatternViews;

namespace Lens.AiProof;

/// <summary>
/// [DENEY] CLIP agirliklarina DOKUNMADAN, yalnizca girdi/kadraj/renk ve
/// skor birlestirme katmanlarini degistirerek desen eslesmesinin
/// iyilesip iyilesmedigini olcer.
///
/// URETIM KODU DEGILDIR. Ciktilarin tamami, git disinda kalan bir calisma
/// dizinine yazilir; kullanicinin katalog klasorune HICBIR SEY yazilmaz.
/// </summary>
public static class ClipPatternExperiment
{
    /// <summary>Bir adayin skoru nasil hesaplanacak - coklu gorunumler tek sayiya nasil indirgenecek.</summary>
    public enum Aggregation
    {
        /// <summary>Herhangi bir sorgu gorunumu ile herhangi bir katalog gorunumu arasindaki EN YUKSEK skor.</summary>
        MaxPair,

        /// <summary>En yuksek iki cift skorunun ortalamasi - tek bir kucuk ortak motifin tum eslesmeyi tasimasini zorlastirir.</summary>
        MeanTop2,

        /// <summary>Global (tam goruntu) benzerligi ile en iyi yerel bolge benzerliginin agirlikli toplami.</summary>
        GlobalPlusMaxLocal,
    }

    /// <param name="Name">Rapor ve profil kimliginde kullanilan kisa ad.</param>
    /// <param name="CatalogViews">Katalog tarafinda saklanacak gorunumler - INDEKS BOYUTUNU ve suresini bunlar belirler.</param>
    /// <param name="QueryViews">Yalnizca sorgu aninda uretilen gorunumler - indeks maliyeti YOK, sorgu gecikmesi var.</param>
    /// <param name="Aggregate">Skor birlestirme.</param>
    /// <param name="Whiten">Katalog ortalamasi cikarilip yeniden normalize edilsin mi (baskin "genel renk/parlaklik" yonunu bastirir).</param>
    /// <param name="GlobalWeight">GlobalPlusMaxLocal icin global terimin agirligi.</param>
    public sealed record Strategy(
        string Name,
        IReadOnlyList<V.ViewSpec> CatalogViews,
        IReadOnlyList<V.ViewSpec> QueryViews,
        Aggregation Aggregate = Aggregation.MaxPair,
        bool Whiten = false,
        double GlobalWeight = 0.5)
    {
        /// <summary>Katalogda gorsel basina saklanan vektor sayisi - indeks boyutunun dogrudan carpani.</summary>
        public int ViewsPerCatalogImage => CatalogViews.Count;

        public IEnumerable<V.ViewSpec> AllViews => CatalogViews.Concat(QueryViews);
    }

    private sealed record CatalogItem(string Name, float[][] Vectors);

    private sealed record QueryCase(string Label, string Path, string SourceName);

    private sealed record EvalResult(
        string Strategy, string Split, int Queries,
        double R1, double R20, double R100, double Mrr,
        double MedianRank, double MedianScore, double MedianMargin,
        Dictionary<string, double> PerTransformR1,
        List<double> CorrectScores);

    // ------------------------------------------------------------------ stratejiler

    public static List<Strategy> BuildStrategies()
    {
        var center = new[] { V.RgbCenter };
        var rot4 = new[] { V.RgbCenter, V.RgbRotation(90), V.RgbRotation(180), V.RgbRotation(270) };
        var grid9 = Enumerable.Range(0, 9).Select(i => V.Grid(i)).ToArray();
        var overlap5 = Enumerable.Range(0, 5).Select(i => V.Overlap(i)).ToArray();

        return new List<Strategy>
        {
            // --- Baseline: bugunku uretim davranisi -------------------------------
            new("A0_baseline_rgb", center, center),

            // --- A) Renk etkisini azaltma -----------------------------------------
            new("A1_gray", new[] { V.GrayCenter }, new[] { V.GrayCenter }),
            new("A2_rgb+gray", new[] { V.RgbCenter, V.GrayCenter }, new[] { V.RgbCenter, V.GrayCenter }),
            new("A3_rgb_whiten", center, center, Whiten: true),
            new("A4_gray_whiten", new[] { V.GrayCenter }, new[] { V.GrayCenter }, Whiten: true),

            // --- B) Donus dayanikliligi -------------------------------------------
            // Once YALNIZCA sorgu tarafinda donus: indeks maliyeti SIFIR.
            new("B1_queryrot4", center, rot4),
            new("B2_queryrot4_gray", new[] { V.GrayCenter },
                new[] { V.GrayCenter, V.GrayRotation(90), V.GrayRotation(180), V.GrayRotation(270) }),
            // Katalogda da donus saklamak 4x indeks demek - kiyas icin.
            new("B3_catalogrot4", rot4, center),

            // --- C) Konum / olcek / kismi desen -----------------------------------
            new("C1_pad", new[] { V.RgbPad }, new[] { V.RgbPad }),
            new("C2_multiscale3", new[] { V.RgbCenter, V.RgbPad, V.RgbZoom },
                new[] { V.RgbCenter, V.RgbPad, V.RgbZoom }),
            new("C3_overlap5", overlap5, overlap5),
            new("C4_grid9", grid9, grid9),
            new("C5_grid9_meantop2", grid9, grid9, Aggregation.MeanTop2),
            new("C6_center+overlap5", new[] { V.RgbCenter }.Concat(overlap5).ToArray(),
                new[] { V.RgbCenter }.Concat(overlap5).ToArray(),
                Aggregation.GlobalPlusMaxLocal),

            // --- D) Birlesik adaylar (gorunum sayisi <= 10 tutuldu) ----------------
            new("D1_gray_multiscale3", new[] { V.GrayCenter, V.GrayPad, V.RgbCenter },
                new[] { V.GrayCenter, V.GrayPad, V.RgbCenter }),
            new("D2_gray_overlap5_queryrot", new[] { V.GrayCenter }.Concat(
                    Enumerable.Range(0, 5).Select(i => V.Overlap(i, gray: true))).ToArray(),
                new[] { V.GrayCenter, V.GrayRotation(90), V.GrayRotation(180), V.GrayRotation(270) }
                    .Concat(Enumerable.Range(0, 5).Select(i => V.Overlap(i, gray: true))).ToArray(),
                Aggregation.GlobalPlusMaxLocal),

            // --- E) 1. tur sonrasi eklenenler --------------------------------------
            // 1. turda whitening TEK BASINA en buyuk kazanci verdi ve indekse
            // hicbir maliyeti yok. Cok gorunumlu yontemlerle DIK (orthogonal)
            // olup olmadigini olcmek icin birlesimleri ayrica denenir - biri
            // digerinin kazancini yutuyor olabilir.
            new("E1_pad_whiten", new[] { V.RgbPad }, new[] { V.RgbPad }, Whiten: true),
            new("E2_multiscale3_whiten", new[] { V.RgbCenter, V.RgbPad, V.RgbZoom },
                new[] { V.RgbCenter, V.RgbPad, V.RgbZoom }, Whiten: true),
            new("E3_overlap5_whiten", overlap5, overlap5, Whiten: true),
            new("E4_center+overlap5_whiten", new[] { V.RgbCenter }.Concat(overlap5).ToArray(),
                new[] { V.RgbCenter }.Concat(overlap5).ToArray(),
                Aggregation.GlobalPlusMaxLocal, Whiten: true),
            new("E5_catalogrot4_whiten", rot4, center, Whiten: true),
        };
    }

    // ------------------------------------------------------------------ ana akis

    public static void Run(
        string repoRoot, string catalogDir, string workDir, string mode, int poolSize,
        string? strategyFilter = null, string? knownQueryPath = null, string? knownTargetName = null,
        string modelKey = "clip")
    {
        // DINOv2 secenegi, "CLIP bu modele alternatif olabilir mi?" sorusunu
        // ADIL biçimde cevaplamak icindir: ayni katalog, ayni sorgular, ayni
        // metrikler, ayni makine. Farkli kosullarda olculmus sayilari
        // karsilastirmak yaniltici olurdu.
        var (modelFile, profile, dimension, outputName) = modelKey.ToLowerInvariant() switch
        {
            "dinov2" => ("dinov2-base.onnx", ImagePreprocessingProfile.DinoV2,
                DinoV2BaseProfile.EmbeddingDimension, DinoV2BaseProfile.OnnxOutputName),
            _ => ("clip-vision-b16-openai.onnx", ImagePreprocessingProfile.Clip,
                ClipEmbedder.EmbeddingDimension, "image_embeds"),
        };

        var modelPath = Path.Combine(repoRoot, "models", modelFile);
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"[HATA] Model bulunamadi: {modelPath}");
            Environment.ExitCode = 1;
            return;
        }

        if (!Directory.Exists(catalogDir))
        {
            Console.WriteLine($"[HATA] Katalog klasoru bulunamadi: {catalogDir}");
            Environment.ExitCode = 1;
            return;
        }

        Directory.CreateDirectory(workDir);
        var queryDir = Path.Combine(workDir, "queries");
        // Onbellek MODEL BASINA ayri: ayni gorunum kimligi (orn. "rgb.center")
        // CLIP'te 512, DINOv2'de 768 boyutlu ve tamamen farkli bir vektordur.
        var cacheDir = Path.Combine(workDir, "cache" + (modelKey == "clip" ? string.Empty : "-" + modelKey));
        Directory.CreateDirectory(queryDir);

        Console.WriteLine("=== [DENEY] CLIP desen odakli iyilestirme ===");
        Console.WriteLine($"Model          : {modelKey}  ({modelFile}, {dimension} boyut, on isleme {profile.Version})");
        Console.WriteLine($"Katalog        : {catalogDir}");
        Console.WriteLine($"Calisma dizini : {workDir}   (git ve katalog DISINDA)");
        Console.WriteLine();

        // ---- Katalog envanteri ----
        var allImages = Directory.EnumerateFiles(catalogDir)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"Katalogdaki desteklenen gorsel sayisi : {allImages.Count}");
        var otherFiles = Directory.EnumerateFiles(catalogDir).Count() - allImages.Count;
        Console.WriteLine($"Desteklenmeyen/atlanan dosya          : {otherFiles}");

        // ---- Birebir kopya gruplari (ground truth dogrulugu icin sart) ----
        var duplicates = BuildDuplicateGroups(allImages);
        var dupGroups = duplicates.Values.Distinct().Count(g => g.Count > 1);
        Console.WriteLine($"Birebir kopya grubu                   : {dupGroups}");
        Console.WriteLine();

        // ---- Havuz + kaynak secimi (deterministik) ----
        var sources = PickDeterministic(allImages, 24);
        var pool = poolSize >= allImages.Count
            ? allImages
            : PickDeterministic(allImages, poolSize).Union(sources).Distinct().ToList();

        var devSources = sources.Where((_, i) => i % 2 == 0).ToList();
        var valSources = sources.Where((_, i) => i % 2 == 1).ToList();
        Console.WriteLine($"Arama havuzu   : {pool.Count} gorsel");
        Console.WriteLine($"Kaynak gorsel  : {sources.Count} (dev {devSources.Count} / val {valSources.Count}) - AYNI kaynagin donusumleri tek kumede kalir");
        Console.WriteLine();

        // ---- Sorgu uretimi ----
        var devQueries = BuildQueries(devSources, queryDir);
        var valQueries = BuildQueries(valSources, queryDir);
        Console.WriteLine($"Sentetik sorgu : dev {devQueries.Count} / val {valQueries.Count}");
        Console.WriteLine();

        var strategies = BuildStrategies();
        if (!string.IsNullOrWhiteSpace(strategyFilter))
        {
            var wanted = strategyFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            strategies = strategies.Where(s => wanted.Contains(s.Name, StringComparer.OrdinalIgnoreCase)).ToList();
            if (strategies.Count == 0)
            {
                Console.WriteLine($"[HATA] Filtreye uyan strateji yok: {strategyFilter}");
                Environment.ExitCode = 1;
                return;
            }
        }

        using var engine = new ClipPatternEngine(modelPath, cacheDir, profile, dimension, outputName);

        // ---- Dogruluk guvencesi: baseline gorunumu uretimle BIREBIR ayni mi? ----
        if (!V.SelfCheckMatchesProduction(pool[0], profile, out var checkDetail))
        {
            Console.WriteLine($"[HATA] Baseline gorunum uretim on islemesiyle AYNI DEGIL ({checkDetail}) - olcum gecersiz olurdu.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"[OK] Baseline gorunum = uretim on islemesi ({checkDetail})");
        Console.WriteLine();

        var results = new List<EvalResult>();
        var knownResults = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalWatch = Stopwatch.StartNew();

        foreach (var strategy in strategies)
        {
            var watch = Stopwatch.StartNew();
            var before = engine.EmbedCallCount;

            var catalog = BuildCatalog(engine, pool, strategy);
            var mean = strategy.Whiten ? ComputeMean(catalog) : null;
            if (mean is not null)
            {
                ApplyWhitening(catalog, mean);
            }

            var dev = Evaluate(engine, strategy, catalog, devQueries, duplicates, mean, "dev");
            var val = Evaluate(engine, strategy, catalog, valQueries, duplicates, mean, "val");
            watch.Stop();
            engine.Flush();

            results.Add(dev);
            results.Add(val);

            var known = EvaluateKnownPair(engine, strategy, catalog, duplicates, mean, knownQueryPath, knownTargetName);
            knownResults[strategy.Name] = known;

            Console.WriteLine(
                $"{strategy.Name,-26} gorunum/gorsel={strategy.ViewsPerCatalogImage,2}  "
                + $"dev R@1={dev.R1,5:F1}% R@20={dev.R20,5:F1}%  |  val R@1={val.R1,5:F1}% R@20={val.R20,5:F1}% R@100={val.R100,5:F1}% MRR={val.Mrr:F3}  "
                + $"| bilinen cift: {known}  "
                + $"[{watch.Elapsed.TotalSeconds,5:F0} sn, {engine.EmbedCallCount - before} yeni embedding]");

            // Uzun turlarda ilerlemenin gorunur olmasi icin acikca bosaltilir -
            // .NET, cikti yonlendirildiginde stdout'u tamponlar.
            Console.Out.Flush();
        }

        totalWatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"Toplam sure: {totalWatch.Elapsed.TotalMinutes:F1} dk  "
            + $"(yeni embedding {engine.EmbedCallCount}, onbellekten {engine.CacheHitCount})");
        Console.WriteLine();

        Console.WriteLine("--- Esik taramasi: dogru hedeflerin yuzde kaci listede kalir (dev+val birlikte) ---");
        Console.Write($"{"strateji",-28}");
        var thresholds = new[] { 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60, 0.65, 0.70, 0.80, 0.90 };
        foreach (var t in thresholds)
        {
            Console.Write($"{t * 100,7:F0}%");
        }

        Console.WriteLine();
        foreach (var strategy in strategies)
        {
            var all = results.Where(r => r.Strategy == strategy.Name).SelectMany(r => r.CorrectScores).ToList();
            if (all.Count == 0)
            {
                continue;
            }

            Console.Write($"{strategy.Name,-28}");
            foreach (var t in thresholds)
            {
                Console.Write($"{all.Count(sc => sc >= t) * 100.0 / all.Count,6:F0}% ");
            }

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("--- Bilinen gercek eslesme cifti (tek yon; yontem SECIMINDE kullanilmadi) ---");
        foreach (var (name, value) in knownResults)
        {
            Console.WriteLine($"  {name,-26} {value}");
        }

        Console.WriteLine();

        WriteDetailedReport(results, strategies, pool.Count);
        var reportPath = Path.Combine(workDir, $"report-{modelKey}-{mode}.md");
        WriteMarkdownReport(reportPath, results, strategies, pool.Count, allImages.Count, knownResults);
        Console.WriteLine($"Ayrintili rapor: {reportPath}");
    }

    // ------------------------------------------------------------------ katalog

    private static List<CatalogItem> BuildCatalog(ClipPatternEngine engine, List<string> pool, Strategy strategy)
    {
        var items = new List<CatalogItem>(pool.Count);
        foreach (var path in pool)
        {
            var embeddings = engine.GetEmbeddings(path, strategy.CatalogViews);
            var vectors = strategy.CatalogViews.Select(v => embeddings[v.ViewId]).ToArray();
            items.Add(new CatalogItem(Path.GetFileName(path), vectors));
        }

        return items;
    }

    /// <summary>Katalogdaki TUM vektorlerin ortalamasi - "whitening" bu yonu bastirmak icin kullanilir.</summary>
    private static float[] ComputeMean(List<CatalogItem> catalog)
    {
        int dim = catalog[0].Vectors[0].Length;
        var sum = new double[dim];
        long count = 0;
        foreach (var item in catalog)
        {
            foreach (var vector in item.Vectors)
            {
                for (int i = 0; i < dim; i++)
                {
                    sum[i] += vector[i];
                }

                count++;
            }
        }

        var mean = new float[dim];
        for (int i = 0; i < dim; i++)
        {
            mean[i] = (float)(sum[i] / Math.Max(1, count));
        }

        return mean;
    }

    private static void ApplyWhitening(List<CatalogItem> catalog, float[] mean)
    {
        foreach (var item in catalog)
        {
            for (int v = 0; v < item.Vectors.Length; v++)
            {
                item.Vectors[v] = Subtract(item.Vectors[v], mean);
            }
        }
    }

    /// <summary>Ortalamayi cikarip yeniden L2 normalize eder. Norm sifira duserse (nadir) vektor oldugu gibi birakilir.</summary>
    private static float[] Subtract(float[] vector, float[] mean)
    {
        var result = new float[vector.Length];
        double sumSquares = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] - mean[i];
            sumSquares += (double)result[i] * result[i];
        }

        var norm = Math.Sqrt(sumSquares);
        if (norm <= 1e-9)
        {
            return vector;
        }

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = (float)(result[i] / norm);
        }

        return result;
    }

    // ------------------------------------------------------------------ sorgular

    /// <summary>Is kuralindaki her donusum icin deterministik bir sorgu gorseli uretir (PNG - JPEG yeniden sikistirmasi olcume karismasin).</summary>
    private static List<QueryCase> BuildQueries(List<string> sources, string queryDir)
    {
        var cases = new List<QueryCase>();
        foreach (var sourcePath in sources)
        {
            var sourceName = Path.GetFileName(sourcePath);
            var stem = Path.GetFileNameWithoutExtension(sourceName);
            var safeStem = new string(stem.Where(char.IsLetterOrDigit).ToArray());
            if (safeStem.Length > 40)
            {
                safeStem = safeStem[..40];
            }

            safeStem += "_" + ShortHash(sourceName);

            void Emit(string label, Action<IImageProcessingContext> op)
            {
                var outPath = Path.Combine(queryDir, $"{safeStem}__{label}.png");
                if (!File.Exists(outPath))
                {
                    using var image = Image.Load<Rgb24>(sourcePath);
                    image.Mutate(op);
                    image.SaveAsPng(outPath);
                }

                cases.Add(new QueryCase(label, outPath, sourceName));
            }

            using (var probe = Image.Load<Rgb24>(sourcePath))
            {
                int w = probe.Width, h = probe.Height;
                int cw = Math.Max(32, w / 2), ch = Math.Max(32, h / 2);

                Emit("hue", x => x.Hue(120));
                Emit("saturation", x => x.Saturate(0.3f));
                Emit("grayscale", x => x.Grayscale());
                Emit("brightness", x => x.Brightness(1.35f));
                Emit("contrast", x => x.Contrast(1.4f));
                Emit("rot90", x => x.Rotate(90));
                Emit("rot180", x => x.Rotate(180));
                Emit("rot270", x => x.Rotate(270));
                Emit("crop_left", x => x.Crop(new Rectangle(0, (h - ch) / 2, cw, ch)));
                Emit("crop_right", x => x.Crop(new Rectangle(w - cw, (h - ch) / 2, cw, ch)));
                Emit("crop_center", x => x.Crop(new Rectangle((w - cw) / 2, (h - ch) / 2, cw, ch)));
                Emit("zoom2x", x => x.Crop(new Rectangle(w / 4, h / 4, Math.Max(32, w / 2), Math.Max(32, h / 2))).Resize(w, h));
                Emit("shrink", x => x.Resize(Math.Max(32, w / 2), Math.Max(32, h / 2)).Pad(w, h, Color.White));
                Emit("offset", x => x.Resize(Math.Max(32, (int)(w * 0.6)), Math.Max(32, (int)(h * 0.6)))
                    .Pad((int)(w * 1.4), (int)(h * 1.4), Color.White)
                    .Crop(new Rectangle(0, 0, w, h)));
            }
        }

        return cases;
    }

    // ------------------------------------------------------------------ degerlendirme

    private static EvalResult Evaluate(
        ClipPatternEngine engine, Strategy strategy, List<CatalogItem> catalog,
        List<QueryCase> queries, Dictionary<string, List<string>> duplicates,
        float[]? mean, string split)
    {
        var ranks = new List<int>();
        var scores = new List<double>();
        var margins = new List<double>();
        var perTransform = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var query in queries)
        {
            var embeddings = engine.GetEmbeddings(query.Path, strategy.QueryViews);
            var queryVectors = strategy.QueryViews.Select(v => embeddings[v.ViewId]).ToArray();
            if (mean is not null)
            {
                queryVectors = queryVectors.Select(v => Subtract(v, mean)).ToArray();
            }

            // Dogru kabul edilen hedefler: kaynak dosya VE onun birebir
            // kopyalari. Kopyayi "yanlis" saymak yapay bir basarisizlik
            // uretirdi (bkz. gorev: etiketlenmemis adayi otomatik yanlis sayma).
            var correct = duplicates.TryGetValue(query.SourceName, out var group)
                ? new HashSet<string>(group, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { query.SourceName };

            var scored = new List<(string Name, double Score)>(catalog.Count);
            foreach (var item in catalog)
            {
                scored.Add((item.Name, ScoreItem(queryVectors, item.Vectors, strategy)));
            }

            scored.Sort((a, b) => b.Score.CompareTo(a.Score));

            int rank = scored.FindIndex(s => correct.Contains(s.Name)) + 1;
            double score = rank > 0 ? scored[rank - 1].Score : 0;
            var bestRival = scored.FirstOrDefault(s => !correct.Contains(s.Name));

            ranks.Add(rank);
            scores.Add(score);
            margins.Add(score - bestRival.Score);

            if (!perTransform.TryGetValue(query.Label, out var list))
            {
                list = new List<int>();
                perTransform[query.Label] = list;
            }

            list.Add(rank);
        }

        double Pct(Func<int, bool> ok) => ranks.Count == 0 ? 0 : ranks.Count(ok) * 100.0 / ranks.Count;
        double Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        return new EvalResult(
            strategy.Name, split, ranks.Count,
            Pct(r => r == 1), Pct(r => r is >= 1 and <= 20), Pct(r => r is >= 1 and <= 100),
            ranks.Count == 0 ? 0 : ranks.Average(r => r > 0 ? 1.0 / r : 0.0),
            Median(ranks.Select(r => (double)r).ToList()), Median(scores), Median(margins),
            perTransform.ToDictionary(kv => kv.Key, kv => kv.Value.Count(r => r == 1) * 100.0 / kv.Value.Count, StringComparer.Ordinal),
            scores);
    }

    /// <summary>
    /// Kullanicinin bildirdigi GERCEK eslesme cifti. Yalnizca TEK yon
    /// gecerlidir: sorgu dosyasi katalogda YOKTUR, hedef katalogdadir. Ters
    /// yon olculmez cunku o dosyanin birebir kopyasi katalogda bulunur ve
    /// sorgu kendi kopyasini %100 ile bulur - bu bir olcum degil, totolojidir.
    /// Bu tek veri noktasi yontem SECIMINDE kullanilmaz (asiri uyum riski),
    /// yalnizca raporlanir.
    /// </summary>
    private static string EvaluateKnownPair(
        ClipPatternEngine engine, Strategy strategy, List<CatalogItem> catalog,
        Dictionary<string, List<string>> duplicates, float[]? mean,
        string? queryPath, string? targetName)
    {
        if (string.IsNullOrWhiteSpace(queryPath) || string.IsNullOrWhiteSpace(targetName) || !File.Exists(queryPath))
        {
            return "atlandi";
        }

        if (!catalog.Any(c => string.Equals(c.Name, targetName, StringComparison.OrdinalIgnoreCase)))
        {
            return "hedef havuzda yok";
        }

        var embeddings = engine.GetEmbeddings(queryPath, strategy.QueryViews);
        var queryVectors = strategy.QueryViews.Select(v => embeddings[v.ViewId]).ToArray();
        if (mean is not null)
        {
            queryVectors = queryVectors.Select(v => Subtract(v, mean)).ToArray();
        }

        var correct = duplicates.TryGetValue(targetName, out var group)
            ? new HashSet<string>(group, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { targetName };

        var scored = catalog
            .Select(item => (item.Name, Score: ScoreItem(queryVectors, item.Vectors, strategy)))
            .OrderByDescending(s => s.Score)
            .ToList();

        int rank = scored.FindIndex(s => correct.Contains(s.Name)) + 1;
        double score = rank > 0 ? scored[rank - 1].Score : 0;
        var rival = scored.FirstOrDefault(s => !correct.Contains(s.Name));
        return $"sira={rank} skor={score:F3} rakip={rival.Score:F3} pay={score - rival.Score:+0.000;-0.000}";
    }

    private static double ScoreItem(float[][] queryVectors, float[][] catalogVectors, Strategy strategy)
    {
        double best = double.MinValue, second = double.MinValue;
        foreach (var q in queryVectors)
        {
            foreach (var c in catalogVectors)
            {
                var dot = Dot(q, c);
                if (dot > best)
                {
                    second = best;
                    best = dot;
                }
                else if (dot > second)
                {
                    second = dot;
                }
            }
        }

        return strategy.Aggregate switch
        {
            Aggregation.MeanTop2 => second == double.MinValue ? best : (best + second) / 2.0,

            // Global terim: her iki tarafin ILK gorunumu (tanim geregi tam
            // goruntu) - tek bir kucuk ortak motifin eslesmeyi tek basina
            // tasimasini zorlastirir.
            Aggregation.GlobalPlusMaxLocal =>
                strategy.GlobalWeight * Dot(queryVectors[0], catalogVectors[0])
                + (1 - strategy.GlobalWeight) * best,

            _ => best,
        };
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    // ------------------------------------------------------------------ yardimcilar

    /// <summary>Sabit arali (deterministik) secim - "sansli" bir alt kume secilmesini onler.</summary>
    private static List<string> PickDeterministic(List<string> items, int count)
    {
        if (count >= items.Count)
        {
            return new List<string>(items);
        }

        var step = (double)items.Count / count;
        return Enumerable.Range(0, count).Select(i => items[(int)(i * step)]).Distinct().ToList();
    }

    /// <summary>Dosya adindan kisa, kararli bir kimlik - sorgu dosya adlarini benzersiz kilar.</summary>
    private static string ShortHash(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();

    /// <summary>
    /// Icerik hash'ine gore birebir kopya gruplari. Ayni desenin iki kopyasi
    /// varsa, sorgu hangisini bulursa bulsun DOGRU sayilmalidir - aksi halde
    /// olcum yapay olarak kotulesirdi.
    /// </summary>
    private static Dictionary<string, List<string>> BuildDuplicateGroups(List<string> images)
    {
        var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in images)
        {
            string hash;
            try
            {
                using var stream = File.OpenRead(path);
                hash = Convert.ToHexString(MD5.HashData(stream));
            }
            catch
            {
                continue;
            }

            if (!byHash.TryGetValue(hash, out var list))
            {
                list = new List<string>();
                byHash[hash] = list;
            }

            list.Add(Path.GetFileName(path));
        }

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in byHash.Values)
        {
            foreach (var name in group)
            {
                result[name] = group;
            }
        }

        return result;
    }

    // ------------------------------------------------------------------ rapor

    private static void WriteDetailedReport(List<EvalResult> results, List<Strategy> strategies, int poolSize)
    {
        Console.WriteLine("--- Dogrulama kumesi (val) - donusum bazinda R@1 ---");
        var transforms = results.SelectMany(r => r.PerTransformR1.Keys).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();

        Console.Write($"{"strateji",-26}");
        foreach (var t in transforms)
        {
            Console.Write($"{t[..Math.Min(7, t.Length)],8}");
        }

        Console.WriteLine();

        foreach (var strategy in strategies)
        {
            var val = results.FirstOrDefault(r => r.Strategy == strategy.Name && r.Split == "val");
            if (val is null)
            {
                continue;
            }

            Console.Write($"{strategy.Name,-26}");
            foreach (var t in transforms)
            {
                Console.Write($"{(val.PerTransformR1.TryGetValue(t, out var v) ? v.ToString("F0", CultureInfo.InvariantCulture) : "-"),7}%");
            }

            Console.WriteLine();
        }

        Console.WriteLine();
    }

    private static void WriteMarkdownReport(
        string path, List<EvalResult> results, List<Strategy> strategies, int poolSize, int catalogSize,
        Dictionary<string, string> knownResults)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CLIP desen deneyi - ham sonuclar");
        sb.AppendLine();
        sb.AppendLine($"- Katalog: {catalogSize} gorsel, arama havuzu: {poolSize}");
        sb.AppendLine($"- Uretim tarihi: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("| Strateji | Gorunum/gorsel | dev R@1 | val R@1 | val R@20 | val R@100 | val MRR | val medyan sira | val medyan pay |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var strategy in strategies)
        {
            var dev = results.FirstOrDefault(r => r.Strategy == strategy.Name && r.Split == "dev");
            var val = results.FirstOrDefault(r => r.Strategy == strategy.Name && r.Split == "val");
            if (dev is null || val is null)
            {
                continue;
            }

            sb.AppendLine($"| `{strategy.Name}` | {strategy.ViewsPerCatalogImage} | {dev.R1:F1}% | "
                + $"{val.R1:F1}% | {val.R20:F1}% | {val.R100:F1}% | {val.Mrr:F3} | {val.MedianRank:F0} | {val.MedianMargin:F3} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Bilinen gercek eslesme cifti (tek yon)");
        sb.AppendLine();
        sb.AppendLine("| Strateji | Sonuc |");
        sb.AppendLine("|---|---|");
        foreach (var (name, value) in knownResults)
        {
            sb.AppendLine($"| `{name}` | {value} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Donusum bazinda R@1 (dogrulama kumesi)");
        sb.AppendLine();
        var transformKeys = results.SelectMany(r => r.PerTransformR1.Keys).Distinct()
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        sb.AppendLine("| Strateji | " + string.Join(" | ", transformKeys) + " |");
        sb.AppendLine("|---" + string.Concat(Enumerable.Repeat("|---:", transformKeys.Count)) + "|");
        foreach (var strategy in strategies)
        {
            var val = results.FirstOrDefault(r => r.Strategy == strategy.Name && r.Split == "val");
            if (val is null)
            {
                continue;
            }

            sb.AppendLine($"| `{strategy.Name}` | " + string.Join(" | ", transformKeys.Select(t =>
                val.PerTransformR1.TryGetValue(t, out var v) ? v.ToString("F0", CultureInfo.InvariantCulture) + "%" : "-")) + " |");
        }

        File.WriteAllText(path, sb.ToString());
    }
}
