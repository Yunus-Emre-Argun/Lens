using System.Diagnostics;
using Lens.Core.Ai;
using Lens.Core.Indexing;
using Lens.Core.Search;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Lens.AiProof;

/// <summary>
/// [PILOT] DINOv2-Base entegrasyon smoke testi. AMACI, buyuk benchmarki
/// yeniden kosmak DEGIL (bkz. docs/MODEL_BENCHMARK.md - mevcut olcumler temel
/// alinir); entegrasyonun GERCEK URETIM YOLUNDA (.NET + ONNX + profil-ayrimli
/// index + SimilaritySearch) dogru calistigini ve iş kuralinin (desen kimligi
/// renkten onceliklidir) beklenen yonde korundugunu hizlica gostermektir.
///
/// Olculen davranislar: donus (90/180/270), renk tonu, doygunluk/gri,
/// parlaklik, kismi crop (sol/sag/merkez), olcek/yakinlastirma, desenin
/// goruntudeki KONUMU, ve "ayni motif-farkli renk" vs "ayni renk-farkli
/// motif" siralamasi.
///
/// Hicbir sorgu/gorsel repoya veya publish paketine YAZILMAZ - tum ara
/// dosyalar gecici klasorde uretilir ve sonunda silinir.
/// </summary>
public static class DinoSmokeTest
{
    /// <summary>Is kurali kontrolunun kullandigi donusum etiketi - tek sabit, iki yerde ayri yazilmaz (yazim farki sessizce 0/0 sonuca yol acardi).</summary>
    private const string HueCaseLabel = "04_hue";

    private sealed record QueryCase(string Label, string Path, string ExpectedSource);

    private sealed record CaseResult(string Label, string ExpectedSource, int Rank, float Score, float TopScore, int PoolSize);

    public static void Run(string repoRoot, string? verificationPairDir)
    {
        var modelPath = Path.Combine(repoRoot, "models", DinoV2BaseProfile.ModelFileName);
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"[HATA] Model bulunamadi: {modelPath}");
            Environment.ExitCode = 1;
            return;
        }

        var work = Path.Combine(Path.GetTempPath(), "lens_dino_smoke_" + Guid.NewGuid().ToString("N"));
        var catalogDir = Path.Combine(work, "catalog");
        var queryDir = Path.Combine(work, "queries");
        Directory.CreateDirectory(catalogDir);
        Directory.CreateDirectory(queryDir);

        try
        {
            Console.WriteLine("=== [PILOT] DINOv2-Base entegrasyon smoke testi ===");
            Console.WriteLine();

            // ---- 1) Katalog: gercek klasorler KIRLETILMEZ, gecici kopya kullanilir ----
            var catalogSources = CollectCatalogImages(repoRoot);
            foreach (var source in catalogSources)
            {
                File.Copy(source, Path.Combine(catalogDir, Path.GetFileName(source)), overwrite: true);
            }

            Console.WriteLine($"Katalog     : {catalogSources.Count} gorsel (gecici kopya)");
            Console.WriteLine($"Model       : {DinoV2BaseProfile.ModelId} rev={DinoV2BaseProfile.ModelRevision[..12]}…");

            // ---- 2) Model yukleme suresi (SHA hesabi dahil / haric ayri) ----
            var shaWatch = Stopwatch.StartNew();
            var sha = ModelFileHash.ComputeSha256(modelPath);
            shaWatch.Stop();

            var loadWatch = Stopwatch.StartNew();
            using var embedder = new DinoV2Embedder(modelPath, sha);
            loadWatch.Stop();

            Console.WriteLine($"SHA-256     : {sha}");
            Console.WriteLine();
            Console.WriteLine("--- Hiz olcumleri (CPU) ---");
            Console.WriteLine($"Model dosyasi SHA-256 hesabi : {shaWatch.Elapsed.TotalMilliseconds,8:F0} ms (oturum basina BIR KEZ)");
            Console.WriteLine($"ONNX oturumu olusturma       : {loadWatch.Elapsed.TotalMilliseconds,8:F0} ms");

            // ---- 3) Indeksleme (gercek uretim yolu: profil-ayrimli store + kilit) ----
            var store = ProfiledIndexStore.ForDinoV2Base(embedder.Profile);
            var indexWatch = Stopwatch.StartNew();
            var writeResult = ImageIndex.BuildOrUpdateWithLock(catalogDir, embedder, store);
            indexWatch.Stop();

            if (writeResult.Outcome != IndexWriteOutcome.Updated)
            {
                Console.WriteLine($"[HATA] Indeksleme basarisiz: {writeResult.Outcome}");
                Environment.ExitCode = 1;
                return;
            }

            var entries = writeResult.Entries;
            var perImageMs = indexWatch.Elapsed.TotalMilliseconds / Math.Max(1, entries.Count);
            Console.WriteLine($"Indeksleme ({entries.Count} gorsel)      : {indexWatch.Elapsed.TotalSeconds,8:F1} sn "
                + $"({perImageMs:F0} ms/gorsel)");
            Console.WriteLine($"5.000 gorsel TAHMINI         : {5000 * perImageMs / 1000 / 60,8:F1} dakika "
                + "(dogrusal olceklendirme varsayimi - gercek ag/disk hizi farkli olabilir)");
            Console.WriteLine($"Index dosyasi                : {store.IndexFilePath(catalogDir)}");
            Console.WriteLine($"Eski CLIP index'i            : {LegacyClipIndexStore.Instance.IndexFilePath(catalogDir)} "
                + $"(var mi: {File.Exists(LegacyClipIndexStore.Instance.IndexFilePath(catalogDir))})");

            // ---- 4) Tek gorsel embed + arama suresi ----
            var timingSource = entries.Count > 0 ? Path.Combine(catalogDir, entries[0].RelativePath) : catalogSources[0];
            embedder.Embed(timingSource); // isinma (ilk cagri JIT/allocator etkisi tasir)

            // On isleme ile ONNX cikarimini AYRI olcuyoruz - hangisinin baskin
            // maliyet oldugu belli olmadan "yavas" bir sonucu yorumlamak
            // mumkun degil.
            var preprocessTimes = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var w = Stopwatch.StartNew();
                ImagePreprocessor.PreprocessToChwTensor(timingSource, ImagePreprocessingProfile.DinoV2);
                w.Stop();
                preprocessTimes.Add(w.Elapsed.TotalMilliseconds);
            }

            var embedTimes = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var w = Stopwatch.StartNew();
                embedder.Embed(timingSource);
                w.Stop();
                embedTimes.Add(w.Elapsed.TotalMilliseconds);
            }

            preprocessTimes.Sort();

            var timingEmbedding = embedder.Embed(timingSource);
            var searchTimes = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                var w = Stopwatch.StartNew();
                SimilaritySearch.SearchWithThreshold(timingEmbedding, entries, 0, SimilaritySearch.MaxResults);
                w.Stop();
                searchTimes.Add(w.Elapsed.TotalMilliseconds);
            }

            embedTimes.Sort();
            searchTimes.Sort();
            var medianPre = preprocessTimes[preprocessTimes.Count / 2];
            var medianEmbed = embedTimes[embedTimes.Count / 2];
            Console.WriteLine($"  - on isleme (ImageSharp)    : {medianPre,7:F0} ms");
            Console.WriteLine($"  - ONNX cikarimi (kalan)     : {medianEmbed - medianPre,7:F0} ms");
            Console.WriteLine($"Tek gorsel embedding (medyan) : {medianEmbed,7:F0} ms "
                + $"[kaynak {DescribeImage(timingSource)}]");
            Console.WriteLine($"Arama ({entries.Count} kayit, medyan)      : {searchTimes[searchTimes.Count / 2],7:F1} ms "
                + "(brute-force cosine)");
            Console.WriteLine();

            // ---- 5) Donusum sorgulari uret ----
            var sourceNames = PickSources(entries, count: 5);
            var cases = new List<QueryCase>();
            foreach (var name in sourceNames)
            {
                cases.AddRange(BuildQueryCases(Path.Combine(catalogDir, name), queryDir, name));
            }

            // ---- 6) Her sorgu icin dogru kaynagin sirasi ----
            Console.WriteLine($"--- Donusum dayanikliligi ({sourceNames.Count} kaynak gorsel x {cases.Count / Math.Max(1, sourceNames.Count)} donusum) ---");
            var results = new List<CaseResult>();
            foreach (var c in cases)
            {
                var queryEmbedding = embedder.Embed(c.Path);
                var ranked = SimilaritySearch.SearchWithThreshold(queryEmbedding, entries, 0, SimilaritySearch.MaxResults);
                var rank = ranked.FindIndex(r => string.Equals(r.RelativePath, c.ExpectedSource, StringComparison.OrdinalIgnoreCase)) + 1;
                var score = rank > 0 ? ranked[rank - 1].Score : 0f;
                results.Add(new CaseResult(c.Label, c.ExpectedSource, rank, score, ranked.Count > 0 ? ranked[0].Score : 0f, ranked.Count));
            }

            Console.WriteLine($"{"Donusum",-22} {"R@1",5} {"R@5",5} {"R@20",5} {"medyan sira",12} {"medyan skor",12}");
            foreach (var group in results.GroupBy(r => r.Label).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var list = group.ToList();
                var ranks = list.Select(r => r.Rank).OrderBy(x => x).ToList();
                var scores = list.Select(r => r.Score).OrderBy(x => x).ToList();
                var at1 = list.Count(r => r.Rank == 1) * 100.0 / list.Count;
                var at5 = list.Count(r => r.Rank is >= 1 and <= 5) * 100.0 / list.Count;
                var at20 = list.Count(r => r.Rank is >= 1 and <= 20) * 100.0 / list.Count;
                Console.WriteLine($"{group.Key,-22} {at1,4:F0}% {at5,4:F0}% {at20,4:F0}% "
                    + $"{ranks[ranks.Count / 2],12} {scores[scores.Count / 2],11:P1}");
            }

            Console.WriteLine();

            // ---- 7) Is kurali: ayni motif-farkli renk > ayni renk-farkli motif ----
            Console.WriteLine("--- Is kurali: desen kimligi renkten onceliklidir ---");
            Console.WriteLine("(sorgu = kaynagin RENK TONU DEGISTIRILMIS hali; dogru kaynak, sorguya");
            Console.WriteLine(" RENK olarak en yakin DIGER 5 katalog gorselinden USTTE olmali)");
            var averageColors = entries.ToDictionary(
                e => e.RelativePath,
                e => AverageColor(Path.Combine(catalogDir, e.RelativePath)),
                StringComparer.OrdinalIgnoreCase);

            int ruleOk = 0, ruleTotal = 0;
            foreach (var name in sourceNames)
            {
                var huePath = Path.Combine(queryDir, SafeName(name, HueCaseLabel));
                if (!File.Exists(huePath))
                {
                    continue;
                }

                var queryEmbedding = embedder.Embed(huePath);
                var ranked = SimilaritySearch.SearchWithThreshold(queryEmbedding, entries, 0, SimilaritySearch.MaxResults);
                var rankOfSource = ranked.FindIndex(r => string.Equals(r.RelativePath, name, StringComparison.OrdinalIgnoreCase)) + 1;

                var queryColor = AverageColor(huePath);
                var colorRivals = averageColors
                    .Where(kv => !string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => ColorDistance(kv.Value, queryColor))
                    .Take(5)
                    .Select(kv => kv.Key)
                    .ToList();

                var worstRivalRank = colorRivals
                    .Select(r => ranked.FindIndex(x => string.Equals(x.RelativePath, r, StringComparison.OrdinalIgnoreCase)) + 1)
                    .Where(r => r > 0)
                    .DefaultIfEmpty(int.MaxValue)
                    .Min();

                ruleTotal++;
                var ok = rankOfSource > 0 && rankOfSource < worstRivalRank;
                if (ok)
                {
                    ruleOk++;
                }

                Console.WriteLine($"  {(ok ? "[OK]  " : "[DIKKAT]")} kaynak sirasi={rankOfSource,3}  "
                    + $"en iyi 'ayni renk-farkli motif' sirasi={(worstRivalRank == int.MaxValue ? "-" : worstRivalRank.ToString()),3}  "
                    + $"({Anonymize(name)})");
            }

            Console.WriteLine($"  Sonuc: {ruleOk}/{ruleTotal} kaynakta ayni motif (farkli renk), renk rakiplerinin USTUNDE.");
            Console.WriteLine();

            // ---- 8) Esik etkisi ----
            Console.WriteLine("--- Esik etkisi (donusum sorgularinin dogru kaynak skorlari uzerinde) ---");
            var positiveScores = results.Where(r => r.Rank > 0).Select(r => r.Score).ToList();
            foreach (var threshold in new[] { 40, 50, 55, 60, 70, 80 })
            {
                var kept = positiveScores.Count(sc => sc * 100 >= threshold) * 100.0 / Math.Max(1, positiveScores.Count);
                var marker = threshold == (int)DinoV2BaseProfile.DefaultThresholdPercent ? "  <- pilot varsayilani" : string.Empty;
                Console.WriteLine($"  Esik %{threshold,-3} -> dogru kaynaklarin %{kept,5:F1}'i listede kalir{marker}");
            }

            Console.WriteLine();

            // ---- 9) Kullanicinin bildirdigi dogrulama cifti (anonim) ----
            RunVerificationPair(verificationPairDir, embedder);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch
            {
                Console.WriteLine($"[NOT] Gecici klasor silinemedi: {work}");
            }
        }

        Console.WriteLine("=== Smoke testi bitti ===");
    }

    /// <summary>
    /// Kullanicinin daha once bildirdigi iki gorsel (ayni desenin iki cekimi)
    /// erisilebiliyorsa YALNIZCA yerel dogrulama amaciyla kullanilir. Gercek
    /// dosya adlari raporlanmaz ("dogrulama cifti A/B") ve dosyalar hicbir
    /// yere kopyalanmaz.
    /// </summary>
    private static void RunVerificationPair(string? pairDir, DinoV2Embedder embedder)
    {
        Console.WriteLine("--- Dogrulama cifti (kullanicinin bildirdigi iki gorsel, anonim) ---");
        if (pairDir is null || !Directory.Exists(pairDir))
        {
            Console.WriteLine("  [ATLANDI] dogrulama cifti klasoru erisilemedi");
            return;
        }

        var pair = Directory.EnumerateFiles(pairDir)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Take(2)
            .ToList();

        if (pair.Count < 2)
        {
            Console.WriteLine("  [ATLANDI] klasorde iki gorsel bulunamadi");
            return;
        }

        var a = embedder.Embed(pair[0]);
        var b = embedder.Embed(pair[1]);
        var cosine = a.Zip(b, (x, y) => x * y).Sum();
        Console.WriteLine($"  Dogrulama cifti A <-> B benzerligi: {cosine:P2}");
        Console.WriteLine($"  Pilot esigi (%{DinoV2BaseProfile.DefaultThresholdPercent}) ile listelenir mi: "
            + (cosine * 100 >= DinoV2BaseProfile.DefaultThresholdPercent ? "EVET" : "HAYIR"));
        Console.WriteLine($"  CLIP donemi esigi (%{SimilarityThreshold.DefaultPercent}) ile listelenir miydi: "
            + (cosine * 100 >= SimilarityThreshold.DefaultPercent ? "EVET" : "HAYIR"));
        Console.WriteLine();
    }

    private static List<string> CollectCatalogImages(string repoRoot)
    {
        var images = new List<string>();
        foreach (var folder in new[]
        {
            Path.Combine(repoRoot, "benchmark", "data", "distractors"),
            Path.Combine(repoRoot, "nevresim"),
        })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            images.AddRange(Directory.EnumerateFiles(folder)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
        }

        // Ayni dosya adi iki klasorde varsa kopya cakismasini onle.
        return images
            .GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Sabit (deterministik) aralikli secim - "sansli" bir alt kume secilmesin.</summary>
    private static List<string> PickSources(List<ImageIndexEntry> entries, int count)
    {
        if (entries.Count == 0)
        {
            return new List<string>();
        }

        var step = Math.Max(1, entries.Count / count);
        return entries
            .Where((_, i) => i % step == 0)
            .Take(count)
            .Select(e => e.RelativePath)
            .ToList();
    }

    private static string SafeName(string sourceName, string label) =>
        $"{Path.GetFileNameWithoutExtension(sourceName)}__{label}.png";

    /// <summary>
    /// Is kuralinda adi gecen donusumleri uretir. Hepsi deterministiktir
    /// (rastgelelik yok) ve PNG olarak yazilir - JPEG yeniden sikistirma
    /// kaybi olcumune karismasin.
    /// </summary>
    private static List<QueryCase> BuildQueryCases(string sourcePath, string queryDir, string sourceName)
    {
        var cases = new List<QueryCase>();

        void Emit(string label, Action<IImageProcessingContext> op)
        {
            var outPath = Path.Combine(queryDir, SafeName(sourceName, label));
            using var image = Image.Load<Rgb24>(sourcePath);
            image.Mutate(op);
            image.SaveAsPng(outPath);
            cases.Add(new QueryCase(label, outPath, sourceName));
        }

        using (var probe = Image.Load<Rgb24>(sourcePath))
        {
            int w = probe.Width;
            int h = probe.Height;
            int cw = Math.Max(32, w / 2);
            int ch = Math.Max(32, h / 2);

            Emit("00_identity", _ => { });
            Emit("01_rot90", x => x.Rotate(90));
            Emit("02_rot180", x => x.Rotate(180));
            Emit("03_rot270", x => x.Rotate(270));
            Emit(HueCaseLabel, x => x.Hue(120));
            Emit("05_grayscale", x => x.Saturate(0f));
            Emit("06_desaturate50", x => x.Saturate(0.5f));
            Emit("07_brightness", x => x.Brightness(1.35f));
            Emit("08_contrast", x => x.Contrast(1.4f));

            // Kismi crop: desenin yalnizca bir bolumu (sol / sag / merkez).
            Emit("09_crop_left", x => x.Crop(new Rectangle(0, (h - ch) / 2, cw, ch)));
            Emit("10_crop_right", x => x.Crop(new Rectangle(w - cw, (h - ch) / 2, cw, ch)));
            Emit("11_crop_center", x => x.Crop(new Rectangle((w - cw) / 2, (h - ch) / 2, cw, ch)));

            // Olcek: desen daha BUYUK gorunur (yakinlastirma).
            Emit("12_zoom2x", x => x
                .Crop(new Rectangle(w / 4, h / 4, Math.Max(32, w / 2), Math.Max(32, h / 2)))
                .Resize(w, h));

            // Olcek: desen daha KUCUK gorunur - beyaz zemine yerlestirilmis
            // kucuk bir kopya (ayni zamanda KONUM degisikligi de icerir).
            Emit("13_shrink_center", x => x.Resize(Math.Max(32, w / 2), Math.Max(32, h / 2))
                .Pad(w, h, Color.White));

            // Konum: desen tuvalin SAG ALT kosesinde (Pad ortalar, bu yuzden
            // once padding uygulanip sonra kaydiriliyor).
            Emit("14_offset_corner", x => x
                .Resize(Math.Max(32, (int)(w * 0.6)), Math.Max(32, (int)(h * 0.6)))
                .Pad((int)(w * 1.4), (int)(h * 1.4), Color.White)
                .Crop(new Rectangle(0, 0, w, h)));
        }

        return cases;
    }

    /// <summary>Olcumun hangi buyuklukteki gorselde yapildigini raporlar - "yavas" bir sonuc, girdi cozunurlugu bilinmeden yorumlanamaz.</summary>
    private static string DescribeImage(string path)
    {
        try
        {
            var info = Image.Identify(path);
            return $"{info.Width}x{info.Height}";
        }
        catch
        {
            return "boyut okunamadi";
        }
    }

    private static (double R, double G, double B) AverageColor(string path)
    {
        using var image = Image.Load<Rgb24>(path);
        image.Mutate(x => x.Resize(16, 16));
        double r = 0, g = 0, b = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    r += row[x].R;
                    g += row[x].G;
                    b += row[x].B;
                }
            }
        });

        double n = 16 * 16;
        return (r / n, g / n, b / n);
    }

    private static double ColorDistance((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var dr = a.R - b.R;
        var dg = a.G - b.G;
        var db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    /// <summary>Rapor ciktisinda gercek dosya adi yerine kisa, kimlik tasimayan bir etiket.</summary>
    private static string Anonymize(string fileName) =>
        $"kaynak#{Math.Abs(fileName.GetHashCode()) % 1000:000}";
}
