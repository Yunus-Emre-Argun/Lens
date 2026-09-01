using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lens.Core.Ai;
using Lens.Core.Indexing;
using Lens.Core.Search;

// Faz 3A: minimal .NET AI proof (varsayilan mod).
// Faz 3C: "stresstest" argumaniyla genisletilmis veri seti stres testi
// (bkz. RunStressTest). Ayni calisir/guvenilir derleme cikti yolunu
// paylasmak icin ayri bir proje yerine buraya eklendi (bkz. rapor notu:
// Smart App Control, bu oturumda YENI proje/binary yollarini path bazli
// bloklamisti; policy degistirilmedi, sadece zaten calisir durumdaki
// derleme yolu yeniden kullanildi).

if (args.Length > 0 && args[0] == "stresstest")
{
    RunStressTest();
    return;
}

if (args.Length > 1 && args[0] == "detectchanges")
{
    var sw = Stopwatch.StartNew();
    var summary = ImageIndex.DetectChanges(args[1]);
    sw.Stop();
    Console.WriteLine($"DetectChanges({args[1]}) -> new={summary.NewCount} changed={summary.ChangedCount} "
        + $"removed={summary.RemovedCount} unchanged={summary.UnchangedCount} hasChanges={summary.HasChanges} "
        + $"scanError={(summary.ScanError ?? "yok")} ({sw.Elapsed.TotalMilliseconds:F1} ms)");
    return;
}

string repoRoot = FindRepoRoot();
string productFolder = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "nevresim");
string onnxModelPath = args.Length > 1 ? args[1] : Path.Combine(repoRoot, "models", "clip-vision-b16-openai.onnx");
string variationsFolder = Path.Combine(repoRoot, "benchmark", "data", "variations");

Console.WriteLine("=== Lens Faz 3A - .NET AI Proof ===");
Console.WriteLine($"Urun klasoru : {productFolder}");
Console.WriteLine($"ONNX model   : {onnxModelPath}");
Console.WriteLine();

var loadStopwatch = Stopwatch.StartNew();
using var embedder = new ClipEmbedder(onnxModelPath);
loadStopwatch.Stop();
Console.WriteLine($"[1] Model yuklendi: {loadStopwatch.Elapsed.TotalSeconds:F2} sn");

// --- Ilk indeksleme (cache miss bekleniyor) ---
var buildStopwatch = Stopwatch.StartNew();
var (entries1, stats1) = ImageIndex.BuildOrUpdate(productFolder, embedder);
buildStopwatch.Stop();
ImageIndex.Save(productFolder, entries1);
Console.WriteLine(
    $"[2] Ilk indeksleme: {entries1.Count} gorsel, "
    + $"added={stats1.Added} updated={stats1.Updated} unchanged={stats1.Unchanged} removed={stats1.Removed} "
    + $"({buildStopwatch.Elapsed.TotalSeconds:F2} sn)");
Console.WriteLine(
    $"    [Faz 4B] total_scanned={stats1.TotalFilesScanned} supported_images={stats1.SupportedImagesSeen} "
    + $"unsupported_format={stats1.UnsupportedFormatCount} skipped_non_image={stats1.SkippedNonImageCount} "
    + $"failed={stats1.FailedCount} scan_error={(stats1.ScanError ?? "yok")}");
if (stats1.Issues.Count > 0)
{
    foreach (var issue in stats1.Issues)
    {
        Console.WriteLine($"        [{issue.Kind}] {issue.FileName} ({issue.Extension}): {issue.Reason}");
    }
}
if (entries1.Count > 0)
{
    Console.WriteLine($"    Ort. embedding suresi: {buildStopwatch.Elapsed.TotalMilliseconds / entries1.Count:F1} ms/gorsel");
}

// --- Ikinci calistirma (cache hit bekleniyor: 0 recompute) ---
var rebuildStopwatch = Stopwatch.StartNew();
var (entries2, stats2) = ImageIndex.BuildOrUpdate(productFolder, embedder);
rebuildStopwatch.Stop();
Console.WriteLine(
    $"[3] Ikinci calistirma (cache'ten okuma): {entries2.Count} gorsel, "
    + $"added={stats2.Added} updated={stats2.Updated} unchanged={stats2.Unchanged} removed={stats2.Removed} "
    + $"({rebuildStopwatch.Elapsed.TotalSeconds:F2} sn)");
Console.WriteLine($"    Index dosyasi: {ImageIndex.IndexPath(productFolder)}");
Console.WriteLine();

// --- Query testleri: Faz 2 benchmarkindaki sentetik varyasyonlarla ayni
// sorgulari kosuyoruz (bu dosyalar urun index'ine GIRMEDI, sadece test
// query'si olarak kullaniliyor) ve Python sonuclarini (clip_results.json)
// referans alarak manuel karsilastirma yapiyoruz.
string[] testQueries =
{
    "WhatsApp Image 2026-08-31 at 3.06.46 PM__brightness.jpg",
    "WhatsApp Image 2026-08-31 at 3.06.47 PM (2)__downscale_upscale.jpg",
    "WhatsApp Image 2026-08-31 at 3.06.47 PM (1)__crop.jpg",
};

Console.WriteLine("[4] Query testleri (Top-5):");
foreach (var queryFile in testQueries)
{
    var queryPath = Path.Combine(variationsFolder, queryFile);
    if (!File.Exists(queryPath))
    {
        Console.WriteLine($"    [ATLANDI] bulunamadi: {queryPath}");
        continue;
    }

    var queryStopwatch = Stopwatch.StartNew();
    var queryEmbedding = embedder.Embed(queryPath);
    var top5 = SimilaritySearch.TopK(queryEmbedding, entries2, 5);
    queryStopwatch.Stop();

    Console.WriteLine($"    Query: {queryFile}  ({queryStopwatch.Elapsed.TotalMilliseconds:F0} ms)");
    foreach (var r in top5)
    {
        Console.WriteLine($"        {r.Score:F4}  {r.RelativePath}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Bitti ===");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lens.sln")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? throw new DirectoryNotFoundException("Lens.sln bulunamadi; repo kokunden calistirin.");
}

static void RunStressTest()
{
    // Faz 3C: genisletilmis veri seti stres testi. Mevcut Lens.Core (CLIP
    // ONNX + persistent index + cosine similarity) DEGISTIRILMEDEN
    // kullanilir. Amac: 11 gercek urun + kullanicinin manuel olarak son
    // haline getirdigi distractor seti (~177 gorsel, ~188 aday) arasinda,
    // Faz 2'nin sentetik query'leriyle Top-1/3/5 dogrulugunu ve
    // indexing/arama performansini olcmek.

    string repoRoot = FindRepoRoot();
    string rawFolder = Path.Combine(repoRoot, "benchmark", "data", "raw");
    string distractorsFolder = Path.Combine(repoRoot, "benchmark", "data", "distractors");
    string variationsFolder = Path.Combine(repoRoot, "benchmark", "data", "variations");
    string manifestPath = Path.Combine(repoRoot, "benchmark", "data", "variations_manifest.json");
    string modelPath = Path.Combine(repoRoot, "models", "clip-vision-b16-openai.onnx");
    string reportPath = Path.Combine(repoRoot, "benchmark", "results", "expanded_stress_test.md");

    Console.WriteLine("=== Lens Faz 3C - Expanded Dataset Stress Test ===\n");

    var manifestJson = File.ReadAllText(manifestPath);
    var manifest = JsonSerializer.Deserialize<Dictionary<string, VariationInfo>>(manifestJson)
        ?? throw new InvalidOperationException("manifest okunamadi");

    var loadSw = Stopwatch.StartNew();
    using var embedder = new ClipEmbedder(modelPath);
    loadSw.Stop();
    Console.WriteLine($"[1] Model yuklendi: {loadSw.Elapsed.TotalSeconds:F2} sn\n");

    var (rawEntries, rawStats) = ImageIndex.BuildOrUpdate(rawFolder, embedder);
    ImageIndex.Save(rawFolder, rawEntries);
    Console.WriteLine($"[2] Ground truth (raw) index: {rawEntries.Count} gorsel "
        + $"(yeni={rawStats.Added}, degismeyen={rawStats.Unchanged})\n");

    var progress1 = new Progress<(int Done, int Total)>(p =>
    {
        if (p.Done % 20 == 0 || p.Done == p.Total)
            Console.WriteLine($"    indeksleniyor... {p.Done}/{p.Total}");
    });
    var distractSw1 = Stopwatch.StartNew();
    var (distractEntries1, distractStats1) = ImageIndex.BuildOrUpdate(distractorsFolder, embedder, progress1);
    distractSw1.Stop();
    ImageIndex.Save(distractorsFolder, distractEntries1);
    double avgMsFirst = distractStats1.Added > 0
        ? distractSw1.Elapsed.TotalMilliseconds / distractStats1.Added
        : 0;
    Console.WriteLine($"[3] Distractor index - 1. calistirma: {distractEntries1.Count} gorsel, "
        + $"yeni={distractStats1.Added}, degismeyen={distractStats1.Unchanged}, "
        + $"okunamayan={distractStats1.FailedCount} ({distractSw1.Elapsed.TotalSeconds:F2} sn, "
        + $"ort={avgMsFirst:F0} ms/yeni-gorsel)\n");

    var distractSw2 = Stopwatch.StartNew();
    var (distractEntries2, distractStats2) = ImageIndex.BuildOrUpdate(distractorsFolder, embedder);
    distractSw2.Stop();
    Console.WriteLine($"[4] Distractor index - 2. calistirma (cache-hit beklenir): "
        + $"yeni={distractStats2.Added}, degismeyen={distractStats2.Unchanged} "
        + $"({distractSw2.Elapsed.TotalSeconds:F2} sn)\n");

    var rawNames = new HashSet<string>(rawEntries.Select(e => e.RelativePath), StringComparer.OrdinalIgnoreCase);
    var poolBaseline = rawEntries;
    var poolFull = rawEntries.Concat(distractEntries2).ToList();
    Console.WriteLine($"[5] Havuzlar hazir: baseline={poolBaseline.Count}, full={poolFull.Count}\n");

    var variationFiles = Directory.EnumerateFiles(variationsFolder)
        .Where(f => manifest.ContainsKey(Path.GetFileName(f)))
        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
        .ToList();

    Console.WriteLine($"[6] {variationFiles.Count} query test edilecek (baseline + full havuz)\n");

    var baselineResults = new List<QueryResult>();
    var fullResults = new List<QueryResult>();

    foreach (var queryPath in variationFiles)
    {
        var queryFile = Path.GetFileName(queryPath);
        var info = manifest[queryFile];

        var embedSw = Stopwatch.StartNew();
        var embedding = embedder.Embed(queryPath);
        embedSw.Stop();

        baselineResults.Add(RunOnPool(queryFile, info, embedding, poolBaseline, embedSw.Elapsed.TotalMilliseconds));
        fullResults.Add(RunOnPool(queryFile, info, embedding, poolFull, embedSw.Elapsed.TotalMilliseconds));
    }

    WriteReport(reportPath, poolBaseline, poolFull, baselineResults, fullResults, rawNames,
        distractStats1, distractStats2, distractSw1, distractSw2, avgMsFirst);

    Console.WriteLine("=== Bitti ===");
    Console.WriteLine($"Rapor: {reportPath}");
}

static QueryResult RunOnPool(string queryFile, VariationInfo info, float[] embedding, List<ImageIndexEntry> pool, double embedMs)
{
    var sortSw = Stopwatch.StartNew();
    var ranked = SimilaritySearch.TopK(embedding, pool, pool.Count);
    sortSw.Stop();

    int rank = ranked.FindIndex(r => string.Equals(r.RelativePath, info.Original, StringComparison.OrdinalIgnoreCase)) + 1;
    var top5 = ranked.Take(5).ToList();

    return new QueryResult(
        QueryFile: queryFile,
        VariationType: info.VariationType,
        GroundTruth: info.Original,
        Rank: rank,
        Top5: top5,
        EmbedMs: embedMs,
        SortMs: sortSw.Elapsed.TotalMilliseconds,
        PoolSize: pool.Count);
}

static void WriteReport(
    string reportPath,
    List<ImageIndexEntry> poolBaseline,
    List<ImageIndexEntry> poolFull,
    List<QueryResult> baselineResults,
    List<QueryResult> fullResults,
    HashSet<string> rawNames,
    IndexUpdateStats distractStats1,
    IndexUpdateStats distractStats2,
    Stopwatch distractSw1,
    Stopwatch distractSw2,
    double avgMsFirst)
{
    var sb = new System.Text.StringBuilder();

    static (double top1, double top3, double top5) Accuracy(List<QueryResult> results)
    {
        int n = results.Count;
        double t1 = results.Count(r => r.Rank == 1) * 100.0 / n;
        double t3 = results.Count(r => r.Rank is >= 1 and <= 3) * 100.0 / n;
        double t5 = results.Count(r => r.Rank is >= 1 and <= 5) * 100.0 / n;
        return (t1, t3, t5);
    }

    var (b1, b3, b5) = Accuracy(baselineResults);
    var (f1, f3, f5) = Accuracy(fullResults);
    double avgQueryMsBaseline = baselineResults.Average(r => r.EmbedMs + r.SortMs);
    double avgQueryMsFull = fullResults.Average(r => r.EmbedMs + r.SortMs);

    sb.AppendLine("# Lens Faz 3C - Genisletilmis Veri Seti Stres Testi");
    sb.AppendLine();
    sb.AppendLine("Mevcut C#/.NET Lens.Core + CLIP ONNX pipeline'i (WPF MVP'de kullanilan ayni kod), "
        + "11 gercek urun + kullanicinin manuel olarak son haline getirdigi distractor seti ile test edildi. "
        + "Model/algoritma degisikligi yapilmadi - amac mevcut sistemin buyuyen veri setinde "
        + "gercek davranisini olcmekti.");
    sb.AppendLine();
    sb.AppendLine($"Distractor kaynagi: Openverse (acik lisansli), kullanicinin manuel son "
        + $"duzenlemesiyle **{poolFull.Count - poolBaseline.Count}** gorsel. Toplam aday havuzu: **{poolFull.Count}**.");
    sb.AppendLine();
    sb.AppendLine("## Ozet Tablo");
    sb.AppendLine();
    sb.AppendLine("| Dataset | Top-1 | Top-3 | Top-5 | Avg Query (embed+sort) |");
    sb.AppendLine("|---|---:|---:|---:|---:|");
    sb.AppendLine($"| {poolBaseline.Count} urun (baseline) | {b1:F1}% | {b3:F1}% | {b5:F1}% | {avgQueryMsBaseline:F0} ms |");
    sb.AppendLine($"| ~{poolFull.Count} urun (+distractor) | {f1:F1}% | {f3:F1}% | {f5:F1}% | {avgQueryMsFull:F0} ms |");
    sb.AppendLine();

    sb.AppendLine("## Varyasyon Turune Gore Top-5 (genisletilmis havuz, ~" + poolFull.Count + " aday)");
    sb.AppendLine();
    sb.AppendLine("| Varyasyon | Top-1 | Top-3 | Top-5 | Sorgu sayisi |");
    sb.AppendLine("|---|---:|---:|---:|---:|");
    foreach (var group in fullResults.GroupBy(r => r.VariationType).OrderBy(g => g.Key))
    {
        var n = group.Count();
        double t1 = group.Count(r => r.Rank == 1) * 100.0 / n;
        double t3 = group.Count(r => r.Rank is >= 1 and <= 3) * 100.0 / n;
        double t5 = group.Count(r => r.Rank is >= 1 and <= 5) * 100.0 / n;
        sb.AppendLine($"| {group.Key} | {t1:F0}% | {t3:F0}% | {t5:F0}% | {n} |");
    }
    sb.AppendLine();

    sb.AppendLine("## Indexing Performansi");
    sb.AppendLine();
    sb.AppendLine($"- Distractor ilk indeksleme: {distractStats1.Added} yeni gorsel, "
        + $"{distractSw1.Elapsed.TotalSeconds:F1} sn toplam, ort. {avgMsFirst:F0} ms/gorsel");
    sb.AppendLine($"- Distractor 2. calistirma (cache-hit dogrulamasi): "
        + $"yeni={distractStats2.Added}, degismeyen={distractStats2.Unchanged}, "
        + $"{distractSw2.Elapsed.TotalSeconds:F2} sn (persistent cache calisiyor)");
    if (distractStats1.FailedCount > 0)
    {
        sb.AppendLine($"- Okunamayan/embed edilemeyen distractor: {distractStats1.FailedCount}");
    }
    sb.AppendLine();

    sb.AppendLine("## En Kotu 10 Query (genisletilmis havuzda)");
    sb.AppendLine();
    var worst = fullResults.OrderByDescending(r => r.Rank).Take(10).ToList();
    foreach (var r in worst)
    {
        sb.AppendLine($"### `{r.QueryFile}` ({r.VariationType})");
        sb.AppendLine($"- Doğru ürün: `{r.GroundTruth}`, sırası: **{r.Rank}** / {r.PoolSize}"
            + (r.Rank > 5 ? " (Top-5 DIŞINDA)" : ""));
        sb.AppendLine("- Top-5:");
        foreach (var t in r.Top5)
        {
            var marker = string.Equals(t.RelativePath, r.GroundTruth, StringComparison.OrdinalIgnoreCase) ? " ← doğru ürün" : "";
            var isDistractor = !rawNames.Contains(t.RelativePath);
            sb.AppendLine($"  - {t.Score:F4}  `{t.RelativePath}`{(isDistractor ? " (distractor)" : "")}{marker}");
        }
        sb.AppendLine();
    }

    int droppedOutOfTop5 = fullResults.Count(r => r.Rank > 5);
    sb.AppendLine("## Genel Gözlemler");
    sb.AppendLine();
    sb.AppendLine($"- {fullResults.Count} sorgudan **{droppedOutOfTop5}** tanesinde doğru ürün Top-5 dışına düştü "
        + $"(baseline'da bu sayı {baselineResults.Count(r => r.Rank > 5)} idi).");
    var worstByVariation = fullResults.Where(r => r.Rank > 5)
        .GroupBy(r => r.VariationType)
        .OrderByDescending(g => g.Count());
    if (droppedOutOfTop5 > 0)
    {
        sb.AppendLine("- Top-5 dışına düşenlerin varyasyon türüne dağılımı: "
            + string.Join(", ", worstByVariation.Select(g => $"{g.Key}={g.Count()}")));
    }
    sb.AppendLine();
    sb.AppendLine("_Not: Bu rapor CLIP'in ölçülen davranışını belgeler; ~189 adaylık bir gözlemdir, "
        + "\"1000+ üründe de böyle çalışır\" sonucu çıkarılmamalıdır._");

    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    File.WriteAllText(reportPath, sb.ToString());
}

record VariationInfo(
    [property: JsonPropertyName("original")] string Original,
    [property: JsonPropertyName("variation_type")] string VariationType);

record QueryResult(
    string QueryFile,
    string VariationType,
    string GroundTruth,
    int Rank,
    List<SearchResult> Top5,
    double EmbedMs,
    double SortMs,
    int PoolSize);
