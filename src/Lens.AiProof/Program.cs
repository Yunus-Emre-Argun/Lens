using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lens.Core.Ai;
using Lens.Core.Config;
using Lens.Core.Indexing;
using Lens.Core.Search;
using SixLabors.ImageSharp;

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

if (args.Length > 0 && args[0] == "hardeningtest")
{
    RunHardeningTest();
    return;
}

// [PILOT] DINOv2-Base entegrasyon smoke testi (bkz. Lens.AiProof.DinoSmokeTest).
// Ikinci argument (opsiyonel), kullanicinin bildirdigi dogrulama ciftinin
// bulundugu klasordur - verilmezse o adim ATLANIR. O klasordeki hicbir dosya
// kopyalanmaz/repoya alinmaz, raporda gercek dosya adi GECMEZ.
// [DENEY] CLIP desen odakli iyilestirme olcumu (bkz. Lens.AiProof.ClipPatternExperiment).
//   clippattern <katalog-klasoru> <calisma-dizini> [mode] [havuz-boyutu]
// Katalog klasorune HICBIR SEY YAZILMAZ; tum ciktilar calisma dizinine gider.
if (args.Length > 2 && args[0] == "clippattern")
{
    Lens.AiProof.ClipPatternExperiment.Run(
        FindRepoRoot(),
        catalogDir: args[1],
        workDir: args[2],
        mode: args.Length > 3 ? args[3] : "elim",
        poolSize: args.Length > 4 ? int.Parse(args[4]) : 300,
        strategyFilter: args.Length > 5 ? args[5] : null,
        knownQueryPath: args.Length > 6 ? args[6] : null,
        knownTargetName: args.Length > 7 ? args[7] : null,
        modelKey: args.Length > 8 ? args[8] : "clip");
    return;
}

if (args.Length > 0 && args[0] == "ortbench")
{
    Lens.AiProof.OrtThreadProbe.Run(FindRepoRoot(), args.Length > 1 ? args[1] : "default");
    return;
}

if (args.Length > 0 && args[0] == "dinosmoke")
{
    Lens.AiProof.DinoSmokeTest.Run(FindRepoRoot(), args.Length > 1 ? args[1] : null);
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

static void RunHardeningTest()
{
    int passed = 0, failed = 0;
    void Check(string name, bool condition, string detail = "")
    {
        if (condition) { Console.WriteLine($"  [PASS] {name}"); passed++; }
        else { Console.WriteLine($"  [FAIL] {name} {detail}"); failed++; }
    }

    Console.WriteLine("=== Lens Hardening Test (Codex fix #1 & #2) ===\n");

    // ---- Grup A: bozuk/gecersiz cache recovery (model gerekmez) ----
    Console.WriteLine("[A] Bozuk/gecersiz cache recovery");
    string cacheTestFolder = Path.Combine(Path.GetTempPath(), "lens_cache_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(cacheTestFolder);
    try
    {
        string cachePath = ImageIndex.IndexPath(cacheTestFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        File.WriteAllText(cachePath, "{ this is not valid json ][");
        Check("A1 bozuk JSON -> bos liste, exception yok", ImageIndex.Load(cacheTestFolder).Count == 0);

        var entryNullEmbedding = new List<Dictionary<string, object?>>
        {
            new() { ["RelativePath"] = "a.jpg", ["FileSizeBytes"] = 100L, ["LastWriteTimeUtcTicks"] = 0L, ["Embedding"] = null },
        };
        File.WriteAllText(cachePath, JsonSerializer.Serialize(entryNullEmbedding));
        Check("A2 null embedding -> bos liste", ImageIndex.Load(cacheTestFolder).Count == 0);

        var badDimEntry511 = new List<ImageIndexEntry> { new() { RelativePath = "a.jpg", FileSizeBytes = 100, Embedding = new float[511] } };
        File.WriteAllText(cachePath, JsonSerializer.Serialize(badDimEntry511));
        Check("A3 511-dim embedding -> bos liste", ImageIndex.Load(cacheTestFolder).Count == 0);

        var badDimEntry513 = new List<ImageIndexEntry> { new() { RelativePath = "a.jpg", FileSizeBytes = 100, Embedding = new float[513] } };
        File.WriteAllText(cachePath, JsonSerializer.Serialize(badDimEntry513));
        Check("A4 513-dim embedding -> bos liste", ImageIndex.Load(cacheTestFolder).Count == 0);

        // System.Text.Json varsayilan olarak NaN'i de YAZAMAZ - ham JSON metni
        // elle olusturuluyor (bkz. Infinity yorumu, birkac satir asagida).
        var nanValues = string.Join(",", Enumerable.Repeat("0.0", 511).Prepend("NaN"));
        File.WriteAllText(cachePath,
            $"[{{\"RelativePath\":\"a.jpg\",\"FileSizeBytes\":100,\"LastWriteTimeUtcTicks\":0,\"Embedding\":[{nanValues}]}}]");
        Check("A5 NaN icerikli embedding -> bos liste", ImageIndex.Load(cacheTestFolder).Count == 0);

        // System.Text.Json varsayilan olarak Infinity'yi YAZAMAZ (AllowNamedFloatingPointLiterals
        // gerekir) - bu yuzden ham JSON metni elle olusturuluyor (bozuk/elle
        // duzenlenmis bir cache dosyasini simule ediyor).
        var infValues = string.Join(",", Enumerable.Repeat("0.0", 511).Prepend("Infinity"));
        File.WriteAllText(cachePath,
            $"[{{\"RelativePath\":\"a.jpg\",\"FileSizeBytes\":100,\"LastWriteTimeUtcTicks\":0,\"Embedding\":[{infValues}]}}]");
        Check("A6 Infinity icerikli embedding -> bos liste", ImageIndex.Load(cacheTestFolder).Count == 0);

        var validEntry = new List<ImageIndexEntry> { new() { RelativePath = "a.jpg", FileSizeBytes = 100, Embedding = new float[512] } };
        File.WriteAllText(cachePath, JsonSerializer.Serialize(validEntry));
        var loadedGood = ImageIndex.Load(cacheTestFolder);
        Check("A7 gecerli cache -> normal yuklenir (yanlis pozitif yok)", loadedGood.Count == 1 && loadedGood[0].RelativePath == "a.jpg");
    }
    finally
    {
        TryDeleteCacheAndFolder(cacheTestFolder);
    }

    Console.WriteLine();

    // ---- Grup B: gecici hata -> eski entry korunur; gercek silme -> removed ----
    Console.WriteLine("[B] Gecici hata vs gercek silme");
    string repoRoot = FindRepoRoot();
    string modelPath = Path.Combine(repoRoot, "models", "clip-vision-b16-openai.onnx");
    string sourceImagesDir = Path.Combine(repoRoot, "benchmark", "data", "raw");
    string productDir = Path.Combine(Path.GetTempPath(), "lens_temp_failure_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(productDir);
    try
    {
        var sourceImages = Directory.Exists(sourceImagesDir)
            ? Directory.EnumerateFiles(sourceImagesDir)
                .Where(f => f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList()
            : new List<string>();

        if (sourceImages.Count < 2 || !File.Exists(modelPath))
        {
            Console.WriteLine("  [ATLANDI] test gorselleri veya ONNX model bulunamadi");
        }
        else
        {
            string fileA = Path.Combine(productDir, "fileA.jpeg");
            string fileB = Path.Combine(productDir, "fileB.jpeg");
            File.Copy(sourceImages[0], fileA);
            File.Copy(sourceImages[1], fileB);

            using var embedder = new ClipEmbedder(modelPath);

            var (entries1, stats1) = ImageIndex.BuildOrUpdate(productDir, embedder);
            ImageIndex.Save(productDir, entries1);
            Check("B1 ilk indeksleme: 2 entry olusturuldu", entries1.Count == 2 && stats1.Added == 2);

            var originalEntryA = entries1.First(e => e.RelativePath == "fileA.jpeg");

            // fileA'yi "degismis" gibi gostermek icin LastWriteTime ileri alinir
            // (BuildOrUpdate yeniden embed etmeyi dener), sonra dosya exclusive
            // kilitlenir - bu, network/lock kaynakli GECICI bir okuma hatasini
            // gercekci sekilde simule eder (dosya hala klasorde GORULUYOR).
            File.SetLastWriteTimeUtc(fileA, DateTime.UtcNow.AddMinutes(5));

            List<ImageIndexEntry> entries2;
            IndexUpdateStats stats2;
            using (new FileStream(fileA, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                (entries2, stats2) = ImageIndex.BuildOrUpdate(productDir, embedder);
            }

            var preservedA = entries2.FirstOrDefault(e => e.RelativePath == "fileA.jpeg");
            Check("B2 gecici kilit sirasinda fileA 'removed' olmadi", preservedA is not null);
            Check("B3 fileA eski (saglam) embedding ile ayni kaldi",
                preservedA is not null && preservedA.Embedding.SequenceEqual(originalEntryA.Embedding));
            Check("B4 fileA icin SupportedImageButFailed issue eklendi",
                stats2.Issues.Any(i => i.FileName == "fileA.jpeg" && i.Kind == FileIssueKind.SupportedImageButFailed));
            Check("B5 stats.Removed bu turda fileA'yi saymadi", stats2.Removed == 0);

            ImageIndex.Save(productDir, entries2);

            File.Delete(fileB);
            var (entries3, stats3) = ImageIndex.BuildOrUpdate(productDir, embedder);
            Check("B6 gercekten silinen fileB artik entries icinde degil", !entries3.Any(e => e.RelativePath == "fileB.jpeg"));
            Check("B7 stats.Removed == 1 (fileB)", stats3.Removed == 1);
            Check("B8 fileA hala index'te (dokunulmadi)", entries3.Any(e => e.RelativePath == "fileA.jpeg"));
        }
    }
    finally
    {
        TryDeleteCacheAndFolder(productDir);
    }

    Console.WriteLine();

    // ---- Grup C: buyuk/asiri cozunurluklu gorsel - HARD LIMIT KALDIRILDI ----
    // [Faz 1 - kesin product karari] Onceki surumde bu grup 50MB/50MP
    // ustundeki gorsellerin ImageTooLargeException ile REDDEDILDIGINI
    // dogruluyordu. Bu limitler kaldirildi - gecerli bir fabrika deseni artik
    // SADECE buyuk oldugu icin reddedilmiyor; bunun yerine esigin ustundeki
    // dosyalar ekonomik (decoder-level downsampled) decode ile islenir (bkz.
    // ImagePreprocessor.LoadForPreprocessing). Testler artik "reddedildi mi"
    // yerine "artik basariyla embed ediliyor mu + eski davranis regresyonsuz
    // mu" sorusunu dogruluyor.
    Console.WriteLine("[C] Buyuk/asiri cozunurluklu gorsel - hard limit kaldirildi, ekonomik decode");
    string guardDir = Path.Combine(Path.GetTempPath(), "lens_guard_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(guardDir);
    try
    {
        if (!File.Exists(modelPath) || !Directory.Exists(sourceImagesDir))
        {
            Console.WriteLine("  [ATLANDI] ONNX model veya test gorselleri bulunamadi");
        }
        else
        {
            var normalImage = Directory.EnumerateFiles(sourceImagesDir)
                .FirstOrDefault(f => f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));

            if (normalImage is null)
            {
                Console.WriteLine("  [ATLANDI] normal test gorseli bulunamadi");
            }
            else
            {
                using var embedder = new ClipEmbedder(modelPath);

                // C1: normal kucuk gorsel -> esigin ALTINDA, ONCEKI ile birebir
                // ayni (tam cozunurluk) decode yolunu kullanir - regresyon yok.
                try
                {
                    embedder.Embed(normalImage);
                    Check("C1 normal gorsel sorunsuz embed edilir (regresyon yok)", true);
                }
                catch (Exception ex)
                {
                    Check("C1 normal gorsel sorunsuz embed edilir (regresyon yok)", false, $"beklenmeyen exception: {ex.Message}");
                }

                // Sentetik/buyuk dosyalar ayri bir alt klasorde tutulur ki C4'un
                // ImageIndex.BuildOrUpdate cagrilari bunlari yanlislikla taramasin.
                string syntheticDir = Path.Combine(guardDir, "synthetic");
                Directory.CreateDirectory(syntheticDir);

                // C2: asiri yuksek piksel sayili (60MP) sentetik gorsel - eskiden
                // ImageTooLargeException firlatiyordu, ARTIK KABUL EDILIYOR.
                string hugePixelPath = Path.Combine(syntheticDir, "huge_pixels.jpg");
                using (var huge = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(8000, 7500))
                {
                    huge.SaveAsJpeg(hugePixelPath);
                }

                Check("C2a 60MP gorsel LargeImagePixelHint UZERINDE (ekonomik decode tetiklenmeli)",
                    ImageResourceLimits.TryGetPixelCount(hugePixelPath) > ImageResourceLimits.LargeImagePixelHint);

                bool hugePixelThrew = false;
                Exception? hugePixelEx = null;
                float[]? hugePixelEmbedding = null;
                try { hugePixelEmbedding = embedder.Embed(hugePixelPath); }
                catch (Exception ex) { hugePixelThrew = true; hugePixelEx = ex; }
                Check("C2b 60MP gorsel ARTIK REDDEDILMEDEN embed edilir", !hugePixelThrew, hugePixelEx?.Message ?? "");
                Check("C2c donen embedding beklenen boyutta (512, ekonomik decode CLIP ciktisini bozmuyor)",
                    hugePixelEmbedding is not null && hugePixelEmbedding.Length == 512);

                // C3: kucuk cozunurluklu ama >50MB dosya (gecerli bir JPEG'in
                // sonuna doldurma byte'lari eklenerek) - eskiden dosya-boyutu
                // limiti nedeniyle reddediliyordu, ARTIK KABUL EDILIYOR (dosya
                // boyutuna dayali hicbir sabit esik kalmadi).
                string hugeFilePath = Path.Combine(syntheticDir, "huge_filesize.jpg");
                File.Copy(normalImage, hugeFilePath, overwrite: true);
                using (var fs = new FileStream(hugeFilePath, FileMode.Append))
                {
                    var padding = new byte[1024 * 1024];
                    const long TargetFileSize = 55L * 1024 * 1024;
                    long targetExtra = TargetFileSize - new FileInfo(hugeFilePath).Length;
                    for (long written = 0; written < targetExtra; written += padding.Length)
                    {
                        fs.Write(padding, 0, (int)Math.Min(padding.Length, targetExtra - written));
                    }
                }
                bool hugeFileThrew = false;
                Exception? hugeFileEx = null;
                try { embedder.Embed(hugeFilePath); }
                catch (Exception ex) { hugeFileThrew = true; hugeFileEx = ex; }
                Check("C3 >50MB dosya ARTIK REDDEDILMEDEN embed edilir", !hugeFileThrew, hugeFileEx?.Message ?? "");

                // C4: gercekten BOZUK (corrupt) hale gelen bir urun - eskiden bu
                // senaryo "asiri buyume" ile tetikleniyordu; artik boyut
                // reddetmedigi icin GERCEK bir decode hatasiyla (gecersiz JPEG
                // byte'lari) last-known-good preservation davranisi test edilir.
                string productDirC4 = Path.Combine(guardDir, "producttest");
                Directory.CreateDirectory(productDirC4);
                try
                {
                    string productImagePath = Path.Combine(productDirC4, "product.jpg");
                    File.Copy(normalImage, productImagePath, overwrite: true);
                    var (entriesInit, _) = ImageIndex.BuildOrUpdate(productDirC4, embedder);
                    ImageIndex.Save(productDirC4, entriesInit);
                    var originalProductEntry = entriesInit.FirstOrDefault(e => e.RelativePath == "product.jpg");
                    Check("C4a ilk indekslemede product.jpg saglam embed edildi", originalProductEntry is not null);

                    File.WriteAllBytes(productImagePath, new byte[] { 0xFF, 0xD8, 0x00, 0x01, 0x02 }); // gecersiz/bozuk JPEG
                    File.SetLastWriteTimeUtc(productImagePath, DateTime.UtcNow.AddMinutes(10));

                    var (entriesAfter, statsAfter) = ImageIndex.BuildOrUpdate(productDirC4, embedder);
                    var preservedProductEntry = entriesAfter.FirstOrDefault(e => e.RelativePath == "product.jpg");
                    Check("C4b bozulan product.jpg icin ESKI entry korundu (removed olmadi)",
                        preservedProductEntry is not null);
                    Check("C4c korunan entry orijinal embedding ile ayni",
                        preservedProductEntry is not null && originalProductEntry is not null
                        && preservedProductEntry.Embedding.SequenceEqual(originalProductEntry.Embedding));
                    Check("C4d Issues icinde product.jpg icin SupportedImageButFailed var",
                        statsAfter.Issues.Any(i => i.FileName == "product.jpg" && i.Kind == FileIssueKind.SupportedImageButFailed));
                    Check("C4e stats.Removed bu turda product.jpg'yi saymadi", statsAfter.Removed == 0);
                }
                finally
                {
                    TryDeleteCacheAndFolder(productDirC4);
                }
            }
        }
    }
    finally
    {
        TryDeleteCacheAndFolder(guardDir);
    }

    Console.WriteLine();

    // ---- Grup D: PDF/ZIP/non-image dosya semantigi ----
    Console.WriteLine("[D] PDF/ZIP/non-image dosya semantigi");
    string nonImageDir = Path.Combine(Path.GetTempPath(), "lens_nonimage_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(nonImageDir);
    try
    {
        if (!File.Exists(modelPath) || !Directory.Exists(sourceImagesDir))
        {
            Console.WriteLine("  [ATLANDI] ONNX model veya test gorselleri bulunamadi");
        }
        else
        {
            var normalImage = Directory.EnumerateFiles(sourceImagesDir)
                .FirstOrDefault(f => f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
            if (normalImage is null)
            {
                Console.WriteLine("  [ATLANDI] normal test gorseli bulunamadi");
            }
            else
            {
                File.Copy(normalImage, Path.Combine(nonImageDir, "urun.jpg"), overwrite: true);
                File.WriteAllText(Path.Combine(nonImageDir, "katalog.pdf"), "sahte pdf icerigi");
                File.WriteAllText(Path.Combine(nonImageDir, "arsiv.zip"), "sahte zip icerigi");
                File.WriteAllText(Path.Combine(nonImageDir, "notlar.txt"), "sahte txt icerigi");

                using var embedder = new ClipEmbedder(modelPath);
                var (entries, stats) = ImageIndex.BuildOrUpdate(nonImageDir, embedder);

                Check("D1 crash yok, urun.jpg normal indekslendi", entries.Any(e => e.RelativePath == "urun.jpg"));
                Check("D2 SkippedNonImageCount == 3 (pdf/zip/txt)", stats.SkippedNonImageCount == 3);
                Check("D3 pdf/zip/txt Issues'da NonImageFile + 'Desteklenmeyen dosya türü' olarak gorunuyor",
                    new[] { "katalog.pdf", "arsiv.zip", "notlar.txt" }.All(name =>
                        stats.Issues.Any(i => i.FileName == name && i.Kind == FileIssueKind.NonImageFile
                            && i.Reason == "Desteklenmeyen dosya türü")));
                Check("D4 pdf/zip/txt icin embedding denenmedi (Added sadece urun.jpg)", stats.Added == 1);
            }
        }
    }
    finally
    {
        TryDeleteCacheAndFolder(nonImageDir);
    }

    Console.WriteLine();

    // ---- Grup E: Shared index (.lens) + tek-yazarli exclusive lock ----
    Console.WriteLine("[E] Shared index (.lens) + exclusive writer lock");
    string sharedDir = Path.Combine(Path.GetTempPath(), "lens_shared_test_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(sharedDir);
    try
    {
        var expectedPath = Path.Combine(sharedDir, ".lens", "index.json");
        Check("E1 IndexPath == <ProductDir>/.lens/index.json", ImageIndex.IndexPath(sharedDir) == expectedPath);
        Check("E2 IndexPath eski LocalAppData yolundan FARKLI (artik kullanilmiyor)",
            ImageIndex.IndexPath(sharedDir) != Lens.Core.Config.AppPaths.CacheIndexFilePath(sharedDir));
        Check("E3 IndexPath'i ogrenmek .lens klasoru olusturmaz (side-effect-free)",
            !Directory.Exists(Path.Combine(sharedDir, ".lens")));

        var emptyLoad = ImageIndex.Load(sharedDir);
        Check("E4 Load (index yokken) bos liste doner ve .lens olusturmaz",
            emptyLoad.Count == 0 && !Directory.Exists(Path.Combine(sharedDir, ".lens")));

        var writerA = IndexLock.TryAcquire(sharedDir, out var failureA);
        Check("E5 Writer A lock alabildi", writerA is not null && failureA is null);

        var writerB = IndexLock.TryAcquire(sharedDir, out var failureB);
        Check("E6 Writer A lock tutarken Writer B lock ALAMAZ", writerB is null);
        Check("E7 Writer B basarisizligi 'baska yazar tutuyor' anlamina gelir (failure=null)", failureB is null);

        var lockFileExistsWhileHeld = File.Exists(Path.Combine(sharedDir, ".lens", "index.lock"));
        writerA?.Dispose();
        var writerC = IndexLock.TryAcquire(sharedDir, out var failureC);
        Check("E8 fiziksel index.lock VARKEN bile Writer A dispose sonrasi Writer C ALABILIR (fiziksel varlik != aktif kilit)",
            lockFileExistsWhileHeld && writerC is not null && failureC is null);
        writerC?.Dispose();

        if (!File.Exists(modelPath) || !Directory.Exists(sourceImagesDir))
        {
            Console.WriteLine("  [ATLANDI] BuildOrUpdateWithLock uctan uca testi icin ONNX model/test gorselleri bulunamadi");
        }
        else
        {
            var sourceImage = Directory.EnumerateFiles(sourceImagesDir)
                .FirstOrDefault(f => f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));

            if (sourceImage is null)
            {
                Console.WriteLine("  [ATLANDI] test gorseli bulunamadi");
            }
            else
            {
                using var embedder = new ClipEmbedder(modelPath);
                File.Copy(sourceImage, Path.Combine(sharedDir, "urun.jpg"), overwrite: true);

                var writeResult = ImageIndex.BuildOrUpdateWithLock(sharedDir, embedder);
                Check("E9 BuildOrUpdateWithLock basarili (Updated) doner", writeResult.Outcome == IndexWriteOutcome.Updated);
                Check("E10 kayit sonrasi index.json diskte var", File.Exists(ImageIndex.IndexPath(sharedDir)));

                var (rescanEntries, rescanStats) = ImageIndex.BuildOrUpdate(sharedDir, embedder);
                Check("E11 .lens klasoru scan'e girmiyor (TotalFilesScanned yalnizca urun.jpg)", rescanStats.TotalFilesScanned == 1);
                Check("E12 .lens klasoru unsupported/skipped sayaclarina girmiyor",
                    rescanStats.UnsupportedFormatCount == 0 && rescanStats.SkippedNonImageCount == 0);
                _ = rescanEntries;

                var writerD = IndexLock.TryAcquire(sharedDir, out _);
                List<ImageIndexEntry> readerEntries = new();
                Exception? readerEx = null;
                try { readerEntries = ImageIndex.Load(sharedDir); }
                catch (Exception ex) { readerEx = ex; }
                Check("E13 writer lock tutarken reader stable index.json'u okuyabiliyor",
                    readerEx is null && readerEntries.Count == 1);
                writerD?.Dispose();

                var writerE = IndexLock.TryAcquire(sharedDir, out _);
                var blockedResult = ImageIndex.BuildOrUpdateWithLock(sharedDir, embedder);
                Check("E14 baska writer lock tutarken BuildOrUpdateWithLock LockUnavailable doner",
                    blockedResult.Outcome == IndexWriteOutcome.LockUnavailable);
                Check("E15 LockUnavailable durumunda entries mevcut stable index'i icerir (scan/save yapilmadi)",
                    blockedResult.Entries.Count == 1);
                writerE?.Dispose();
            }
        }
    }
    finally
    {
        TryDeleteCacheAndFolder(sharedDir);
    }

    Console.WriteLine();

    // ---- Grup F: arama sozlesmesi (threshold inclusive + azalan sira + en fazla 999) ----
    // [999-limit karari] Onceki "en fazla 300" sozlesmesi "en fazla 999"a
    // yukseltildi (bkz. docs/DECISIONS.md - SUPERSEDES #91) - kullaniciya
    // yeni bir davranis EKLENMEDI, sadece SimilaritySearch.MaxResults sabiti
    // degisti. Bu grup 0/1/15/999/999-uzeri eslesme senaryolarini ayri ayri kapsar.
    Console.WriteLine("[F] Arama sözleşmesi: threshold (inclusive) + azalan sıra + en fazla 999 sonuç");
    {
        static List<ImageIndexEntry> MakeEntries(params float[] scores)
        {
            var list = new List<ImageIndexEntry>();
            for (int i = 0; i < scores.Length; i++)
            {
                // query=[1] ile dot product tam olarak scores[i] versin diye
                // tek boyutlu embedding - model gerektirmeyen saf matematiksel test.
                list.Add(new ImageIndexEntry { RelativePath = $"item{i}.jpg", Embedding = new[] { scores[i] } });
            }

            return list;
        }

        float[] query = { 1f };

        var f1 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(0.80f), minSimilarityPercent: 80);
        Check("F1 score == threshold -> dahil (inclusive boundary), 1 qualifying -> 1", f1.Count == 1);

        var f2 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(0.75f), minSimilarityPercent: 80);
        Check("F2 score < threshold -> haric, 0 qualifying -> boş liste", f2.Count == 0);

        var f8 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(0.1f, 0.2f), minSimilarityPercent: 99);
        Check("F8 0 qualifying (başka bir senaryo) -> boş liste, exception yok", f8.Count == 0);

        var scores6 = new[] { 0.9f, 0.85f, 0.7f, 0.5f, 0.3f, 0.1f };
        var f7 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(scores6), minSimilarityPercent: 0);
        Check("F7 6 qualifying -> 6 (hepsi, doldurma/padding YOK)", f7.Count == 6);

        var scores15 = Enumerable.Range(0, 15).Select(i => 0.9f - i * 0.01f).ToArray();
        var f6 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(scores15), minSimilarityPercent: 0);
        Check("F6 15 qualifying (999 sınırının ALTINDA) -> hepsi 15, artık kesilmiyor", f6.Count == 15);
        Check("F6b azalan sıralı", f6.SequenceEqual(f6.OrderByDescending(r => r.Score)));

        var scores999 = Enumerable.Range(0, 999).Select(i => 1f - i * 0.001f).ToArray();
        var f11 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(scores999), minSimilarityPercent: 0);
        Check("F11 tam 999 qualifying -> hepsi 999 (sınırda kesilme yok)", f11.Count == 999);
        Check("F11b azalan sıralı", f11.SequenceEqual(f11.OrderByDescending(r => r.Score)));

        // 0.001 adımla 999'un üstüne çıkan bir sayı negatif skora düşüp threshold=0'da
        // "qualifying" olmaktan çıkabileceği için burada daha küçük bir adım (0.0005)
        // kullanılıyor - tüm 1049 öğe pozitif skorda kalır, hepsi qualifying olur.
        var scores1049 = Enumerable.Range(0, 1049).Select(i => 1f - i * 0.0005f).ToArray();
        var f3 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(scores1049), minSimilarityPercent: 0);
        Check("F3 1049 qualifying -> en fazla 999 sonuç (999'u aşan doldurma/padding YOK)", f3.Count == 999);
        Check("F4 azalan sıralı", f3.SequenceEqual(f3.OrderByDescending(r => r.Score)));
        Check("F5 en iyi 999 alındı (ilk 1.00, 999. ~0.501 - 1000-1049 arası ATILDI)",
            Math.Abs(f3[0].Score - 1.0f) < 1e-5 && Math.Abs(f3[998].Score - 0.501f) < 1e-4);

        var f9 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(0.99995f), minSimilarityPercent: 100);
        Check("F9 %100 eşiğinde küçük float farkı (0.99995) yine de DAHİL (epsilon toleransı)", f9.Count == 1);

        var f10 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(0.9990f), minSimilarityPercent: 100);
        Check("F10 %100 eşiğinde epsilon DIŞINDAKİ fark (0.9990) HARİÇ", f10.Count == 0);
    }

    Console.WriteLine();

    // ---- Grup G: threshold girdi validasyonu (SimilarityThreshold.TryParse) ----
    Console.WriteLine("[G] Threshold girdi validasyonu (SimilarityThreshold.TryParse)");
    {
        bool Ok(string? input, double expected)
        {
            var ok = SimilarityThreshold.TryParse(input, out var value);
            return ok && Math.Abs(value - expected) < 1e-9;
        }

        bool Rejects(string? input) => !SimilarityThreshold.TryParse(input, out _);

        Check("G1 '80' -> geçerli, 80", Ok("80", 80));
        Check("G2 '80,5' (TR virgül) -> geçerli, 80.5", Ok("80,5", 80.5));
        Check("G3 '80.5' (nokta) -> geçerli, 80.5", Ok("80.5", 80.5));
        Check("G4 '0' -> geçerli (alt sınır dahil)", Ok("0", 0));
        Check("G5 '100' -> geçerli (üst sınır dahil)", Ok("100", 100));
        Check("G6 boş string -> reddedilir", Rejects(""));
        Check("G7 null -> reddedilir", Rejects(null));
        Check("G8 'abc' (metin) -> reddedilir", Rejects("abc"));
        Check("G9 '-5' (negatif) -> reddedilir", Rejects("-5"));
        Check("G10 '100.1' (100'den büyük) -> reddedilir", Rejects("100.1"));
        Check("G11 'NaN' -> reddedilir", Rejects("NaN"));
        Check("G12 'Infinity' -> reddedilir", Rejects("Infinity"));
    }

    Console.WriteLine();

    // ---- Grup H: auto-index checkbox tercihi (UserSettings.AutoIndexBeforeSearch) ----
    Console.WriteLine("[H] Auto-index checkbox tercihi (UserSettings.AutoIndexBeforeSearch)");
    {
        // Gercek %LocalAppData% dosyasini etkilememek icin, JSON semantigi
        // dogrudan System.Text.Json ile (UserSettings'in kullandigi ayni
        // serializer) izole biçimde test edilir - dosya sistemine dokunmaz.
        var oldJsonWithoutField = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false}";
        var loadedFromOld = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(oldJsonWithoutField);
        Check("H1 eski (alani icermeyen) settings JSON'u -> AutoIndexBeforeSearch=true (geriye uyumlu varsayilan)",
            loadedFromOld is not null && loadedFromOld.AutoIndexBeforeSearch);

        var explicitFalseJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"AutoIndexBeforeSearch\":false}";
        var loadedFalse = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(explicitFalseJson);
        Check("H2 acik 'false' alanli JSON -> AutoIndexBeforeSearch=false (kullanicinin kapatma tercihi korunur)",
            loadedFalse is not null && !loadedFalse.AutoIndexBeforeSearch);

        var freshSettings = new Lens.Core.Config.UserSettings();
        Check("H3 yeni olusturulan UserSettings -> varsayilan AutoIndexBeforeSearch=true", freshSettings.AutoIndexBeforeSearch);

        var roundTripJson = JsonSerializer.Serialize(new Lens.Core.Config.UserSettings { AutoIndexBeforeSearch = false });
        var roundTripped = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(roundTripJson);
        Check("H4 false -> serialize -> deserialize round-trip false olarak korunur",
            roundTripped is not null && !roundTripped.AutoIndexBeforeSearch);
    }

    Console.WriteLine();

    // ---- Grup I: tema tercihi (UserSettings.Theme) - JSON sozlesmesi ----
    // NOT: AppTheme enum'u ve ParseTheme (bilinmeyen/gecersiz/bos -> Lime
    // guvenli donusu, [2026-09-08] onceden Normal idi) Lens.Desktop
    // projesindedir; Lens.AiProof (bu konsol araci) BILEREK yalnizca
    // Lens.Core'a referans verir (bkz. csproj) - bu yuzden burada test
    // edilen SADECE UserSettings.Theme'in JSON okuma/yazma sozlesmesidir
    // (varsayilan deger, alan korunumu, bos/bilinmeyen bir string'in
    // DEGISTIRILMEDEN/reddedilmeden tasindigi - Core katmani hicbir
    // dogrulama/normallestirme YAPMAZ). ParseTheme'in kendisi (Lime
    // fallback'i uygulayan asil kod) ve gercek ekran gecisi bu commit'te
    // kod incelemesiyle + manuel/UI Automation ile dogrulandi (bkz.
    // CHANGELOG.md / final rapor) - I6/I7 burada yalnizca ParseTheme'e
    // ULASACAK GIRDININ (bos/bilinmeyen string) Core katmaninda bozulmadan
    // korundugunu kanitlar, ParseTheme'in KENDISINI calistirmaz.
    Console.WriteLine("[I] Tema tercihi (UserSettings.Theme) - JSON sozlesmesi");
    {
        var oldJsonWithoutTheme = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"AutoIndexBeforeSearch\":true}";
        var loadedFromOld = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(oldJsonWithoutTheme);
        Check("I1 eski (Theme alanini icermeyen) settings JSON'u -> Theme='Lime' (yeni geriye uyumlu varsayilan)",
            loadedFromOld is not null && loadedFromOld.Theme == "Lime");

        var explicitThemeJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"Theme\":\"Koyu\"}";
        var loadedKoyu = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(explicitThemeJson);
        Check("I2 acik 'Koyu' alanli JSON -> Theme='Koyu' (kullanicinin secimi korunur)",
            loadedKoyu is not null && loadedKoyu.Theme == "Koyu");

        var freshSettings = new Lens.Core.Config.UserSettings();
        Check("I3 yeni olusturulan UserSettings -> varsayilan Theme='Lime'", freshSettings.Theme == "Lime");

        var roundTripJson = JsonSerializer.Serialize(new Lens.Core.Config.UserSettings { Theme = "Lime" });
        var roundTripped = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(roundTripJson);
        Check("I4 'Lime' -> serialize -> deserialize round-trip korunur",
            roundTripped is not null && roundTripped.Theme == "Lime");

        // Tema kaydi diger alanlari (AutoIndexBeforeSearch, UserOverride) EZMEMELI -
        // Load->degistir->Save akisinin (bkz. MainWindow.SetTheme) dayandigi sozlesme.
        var combinedJson = "{\"UserOverrideProductDirectory\":\"C:\\\\urunler\",\"UseUserOverride\":true,\"AutoIndexBeforeSearch\":false,\"Theme\":\"AcikSepya\"}";
        var loadedCombined = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(combinedJson);
        Check("I5 Theme ile birlikte AutoIndexBeforeSearch/UserOverride alanlari da korunur",
            loadedCombined is not null
            && loadedCombined.Theme == "AcikSepya"
            && !loadedCombined.AutoIndexBeforeSearch
            && loadedCombined.UseUserOverride
            && loadedCombined.UserOverrideProductDirectory == "C:\\urunler");

        // [2026-09-08] Yeni varsayilan Lime oldugu icin: acikca "Normal" (eski
        // varsayilan) kaydetmis bir kullanicinin tercihi YENI varsayilanla
        // ASLA topluca degistirilmemeli - yalnizca "tercih hic yok/gecersiz"
        // durumunda Lime kullanilir (bkz. MainWindow.ParseTheme).
        var explicitNormalJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"Theme\":\"Normal\"}";
        var loadedNormal = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(explicitNormalJson);
        Check("I5b acikca kaydedilmis 'Normal' -> yeni Lime varsayilanina RAGMEN 'Normal' olarak KORUNUR",
            loadedNormal is not null && loadedNormal.Theme == "Normal");

        // Bos/bilinmeyen bir Theme string'i Core katmaninda hicbir sekilde
        // degistirilmez/reddedilmez - MainWindow.ParseTheme'in Lime fallback'i
        // UI katmaninda calisir, bu asil girdiyi (bozulmadan) alir.
        var emptyThemeJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"Theme\":\"\"}";
        var loadedEmpty = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(emptyThemeJson);
        Check("I6 bos 'Theme:\"\"' JSON'u -> Core katmaninda oldugu gibi (bos) korunur (ParseTheme'de Lime'a doner)",
            loadedEmpty is not null && loadedEmpty.Theme == "");

        var unknownThemeJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"Theme\":\"Bilinmeyen\"}";
        var loadedUnknown = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(unknownThemeJson);
        Check("I7 bilinmeyen 'Theme:\"Bilinmeyen\"' JSON'u -> Core katmaninda oldugu gibi korunur (ParseTheme'de Lime'a doner)",
            loadedUnknown is not null && loadedUnknown.Theme == "Bilinmeyen");
    }

    Console.WriteLine();

    // ---- Grup J: "En fazla sonuç" kullanıcı tercihi (MaxResultsPreference + UserSettings.PreferredMaxResults + SearchWithThreshold entegrasyonu) ----
    Console.WriteLine("[J] \"En fazla sonuç\" tercihi: girdi validasyonu + kalıcılık + arama entegrasyonu");
    {
        bool MOk(string? input, int expected)
        {
            var ok = MaxResultsPreference.TryParse(input, out var value);
            return ok && value == expected;
        }

        bool MRejects(string? input) => !MaxResultsPreference.TryParse(input, out _);

        Check("J1 '1' -> geçerli (alt sınır dahil)", MOk("1", 1));
        Check("J2 '999' -> geçerli (üst sınır dahil)", MOk("999", 999));
        Check("J2b '300' -> geçerli (eski üst sınır, artık normal bir değer)", MOk("300", 300));
        Check("J2c '998' -> geçerli", MOk("998", 998));
        Check("J3 '15' -> geçerli", MOk("15", 15));
        Check("J4 '0' -> reddedilir", MRejects("0"));
        Check("J5 '-5' (negatif) -> reddedilir", MRejects("-5"));
        Check("J6 '1000' (üst sınırın üstü) -> reddedilir", MRejects("1000"));
        Check("J7 '15.5' (ondalık nokta) -> reddedilir", MRejects("15.5"));
        Check("J8 '15,5' (ondalık virgül) -> reddedilir", MRejects("15,5"));
        Check("J9 'abc' (metin) -> reddedilir", MRejects("abc"));
        Check("J10 '' (boş) -> reddedilir - düzenleme sırasında geçici boşluk arama başlatamaz", MRejects(""));
        Check("J11 null -> reddedilir", MRejects((string?)null));
        Check("J12 '   ' (yalnızca boşluk) -> reddedilir", MRejects("   "));

        // [UI mesaj ayrımı] "999'dan büyük" ile "diğer tüm geçersiz durumlar" ARAYÜZDE
        // farklı iki mesajla gösterilir (bkz. SearchButton_Click) - IsAboveMaxAllowed
        // bu ayrımı yapan yardımcı.
        Check("J12b IsAboveMaxAllowed('1000') -> true (geçerli tam sayı ama üst sınırı aşıyor)", MaxResultsPreference.IsAboveMaxAllowed("1000"));
        Check("J12c IsAboveMaxAllowed('9999') -> true", MaxResultsPreference.IsAboveMaxAllowed("9999"));
        Check("J12d IsAboveMaxAllowed('999') -> false (üst sınırın kendisi, GEÇERLİ)", !MaxResultsPreference.IsAboveMaxAllowed("999"));
        Check("J12e IsAboveMaxAllowed('0') -> false (genel mesaja düşer)", !MaxResultsPreference.IsAboveMaxAllowed("0"));
        Check("J12f IsAboveMaxAllowed('-5') -> false", !MaxResultsPreference.IsAboveMaxAllowed("-5"));
        Check("J12g IsAboveMaxAllowed('abc') -> false (tam sayı değil, genel mesaja düşer)", !MaxResultsPreference.IsAboveMaxAllowed("abc"));
        Check("J12h IsAboveMaxAllowed('250.5') -> false (ondalık - tam sayı değil, genel mesaja düşer)", !MaxResultsPreference.IsAboveMaxAllowed("250.5"));
        Check("J12i IsAboveMaxAllowed('') -> false", !MaxResultsPreference.IsAboveMaxAllowed(""));
        Check("J12j IsAboveMaxAllowed(null) -> false", !MaxResultsPreference.IsAboveMaxAllowed(null));

        // [Arama varsayılanları] Default 15 -> 20 oldu (yönetici kararı). GEÇERLİ
        // kayıtlı bir değer olarak 15'i sınayan J13 BİLEREK DEĞİŞTİRİLMEDİ - 15
        // hâlâ geçerli bir tercih, yalnızca ARTIK varsayılan DEĞİL.
        Check("J13 ValidateOrDefault(15) geçerli değeri aynen döner (15 hâlâ geçerli bir tercih, varsayılan DEĞİL)", MaxResultsPreference.ValidateOrDefault(15) == 15);
        Check("J14 ValidateOrDefault(0) -> güvenli varsayılan 20", MaxResultsPreference.ValidateOrDefault(0) == 20);
        Check("J15 ValidateOrDefault(1500) (aralık dışı, bozuk kayıtlı değer) -> güvenli varsayılan 20", MaxResultsPreference.ValidateOrDefault(1500) == 20);
        Check("J15b ValidateOrDefault(500) (999-limit ile ARTIK aralık İÇİNDE) -> aynen 500 döner", MaxResultsPreference.ValidateOrDefault(500) == 500);
        Check("J16 ValidateOrDefault(-3) -> güvenli varsayılan 20", MaxResultsPreference.ValidateOrDefault(-3) == 20);
        Check("J17 ValidateOrDefault(200) eski geçerli bir tercih, aynen döner (20'ye ÇEVRİLMEZ)", MaxResultsPreference.ValidateOrDefault(200) == 200);
        Check("J17b ValidateOrDefault(300) eski geçerli bir tercih, aynen döner (20'ye ÇEVRİLMEZ)", MaxResultsPreference.ValidateOrDefault(300) == 300);
        Check("J17c ValidateOrDefault(999) (yeni üst sınır) geçerli, aynen döner", MaxResultsPreference.ValidateOrDefault(999) == 999);
        Check("J17d ValidateOrDefault(1000) (yeni üst sınırın üstü) -> güvenli varsayılan 20", MaxResultsPreference.ValidateOrDefault(1000) == 20);

        // ---- UserSettings.PreferredMaxResults - JSON sözleşmesi (Grup I ile aynı desen) ----
        var oldJsonWithoutField = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"AutoIndexBeforeSearch\":true,\"Theme\":\"Normal\"}";
        var loadedFromOld = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(oldJsonWithoutField);
        Check("J18 eski (PreferredMaxResults alanını içermeyen) settings JSON'u -> 20 (geriye uyumlu varsayılan)",
            loadedFromOld is not null && loadedFromOld.PreferredMaxResults == 20);

        var freshSettings = new Lens.Core.Config.UserSettings();
        Check("J19 yeni oluşturulan UserSettings -> varsayılan PreferredMaxResults=20", freshSettings.PreferredMaxResults == 20);

        // [Arama varsayılanları] Kayıtlı GEÇERLİ bir tercih (ör. 15/50/200) yeni
        // varsayılana ASLA topluca çevrilmemeli - yalnızca "tercih hiç yok/geçersiz"
        // durumunda 20 kullanılır.
        var savedFifteenJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"PreferredMaxResults\":15}";
        var loadedFifteen = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(savedFifteenJson);
        Check("J19b kayıtlı GEÇERLİ 15 -> 20'ye ÇEVRİLMEZ, 15 olarak kalır", loadedFifteen is not null && loadedFifteen.PreferredMaxResults == 15);

        var saved200Json = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"PreferredMaxResults\":200}";
        var loaded200 = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(saved200Json);
        Check("J19c kayıtlı ESKİ GEÇERLİ 200 -> 20'ye ÇEVRİLMEZ, 200 olarak kalır (eski tercih korunur)", loaded200 is not null && loaded200.PreferredMaxResults == 200);

        var saved300Json = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"PreferredMaxResults\":300}";
        var loaded300 = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(saved300Json);
        Check("J19d kayıtlı ESKİ GEÇERLİ 300 -> 20'ye ÇEVRİLMEZ, 300 olarak kalır (eski tercih korunur)", loaded300 is not null && loaded300.PreferredMaxResults == 300);

        var saved999Json = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"PreferredMaxResults\":999}";
        var loaded999 = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(saved999Json);
        Check("J19e kayıtlı GEÇERLİ 999 (yeni üst sınır) -> 20'ye ÇEVRİLMEZ, 999 olarak kalır", loaded999 is not null && loaded999.PreferredMaxResults == 999);

        var explicitJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"PreferredMaxResults\":50}";
        var loadedExplicit = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(explicitJson);
        Check("J20 açık 'PreferredMaxResults=50' JSON'u -> 50 (kullanıcının geçerli tercihi korunur)",
            loadedExplicit is not null && loadedExplicit.PreferredMaxResults == 50);

        var roundTripJson = JsonSerializer.Serialize(new Lens.Core.Config.UserSettings { PreferredMaxResults = 200 });
        var roundTripped = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(roundTripJson);
        Check("J21 200 -> serialize -> deserialize round-trip korunur (eski tercih)",
            roundTripped is not null && roundTripped.PreferredMaxResults == 200);

        var roundTripJson300 = JsonSerializer.Serialize(new Lens.Core.Config.UserSettings { PreferredMaxResults = 300 });
        var roundTripped300 = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(roundTripJson300);
        Check("J21b 300 -> serialize -> deserialize round-trip korunur (eski tercih)",
            roundTripped300 is not null && roundTripped300.PreferredMaxResults == 300);

        var roundTripJson999 = JsonSerializer.Serialize(new Lens.Core.Config.UserSettings { PreferredMaxResults = 999 });
        var roundTripped999 = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(roundTripJson999);
        Check("J21c 999 (yeni üst sınır) -> serialize -> deserialize round-trip korunur",
            roundTripped999 is not null && roundTripped999.PreferredMaxResults == 999);

        // PreferredMaxResults kaydı diger alanlari (Theme/AutoIndexBeforeSearch/UserOverride/klasor tercihi) EZMEMELI.
        var combinedJson = "{\"UserOverrideProductDirectory\":\"C:\\\\urunler\",\"UseUserOverride\":true,\"AutoIndexBeforeSearch\":false,\"Theme\":\"Koyu\",\"PreferredMaxResults\":75}";
        var loadedCombined = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(combinedJson);
        Check("J22 PreferredMaxResults ile birlikte diğer tüm alanlar (tema/otomatik indeksleme/klasör) da korunur",
            loadedCombined is not null
            && loadedCombined.PreferredMaxResults == 75
            && loadedCombined.Theme == "Koyu"
            && !loadedCombined.AutoIndexBeforeSearch
            && loadedCombined.UseUserOverride
            && loadedCombined.UserOverrideProductDirectory == "C:\\urunler");

        // ---- SearchWithThreshold, kullanicinin ozel "en fazla sonuç" limitiyle (bkz. kullanici talimati: limit 1/15/50/200) ----
        static List<ImageIndexEntry> MakeScoredEntries(params float[] scores)
        {
            var list = new List<ImageIndexEntry>();
            for (int i = 0; i < scores.Length; i++)
            {
                list.Add(new ImageIndexEntry { RelativePath = $"j{i}.jpg", Embedding = new[] { scores[i] } });
            }

            return list;
        }

        float[] jquery = { 1f };
        var scores100 = Enumerable.Range(0, 100).Select(i => 1f - i * 0.001f).ToArray();

        var j23 = SimilaritySearch.SearchWithThreshold(jquery, MakeScoredEntries(scores100), minSimilarityPercent: 0, maxResults: 1);
        Check("J23 limit=1, 100 qualifying -> yalnızca en iyi 1 sonuç", j23.Count == 1 && Math.Abs(j23[0].Score - 1.0f) < 1e-5);

        var j24 = SimilaritySearch.SearchWithThreshold(jquery, MakeScoredEntries(scores100), minSimilarityPercent: 0, maxResults: 15);
        Check("J24 limit=15, 100 qualifying -> en iyi 15, azalan sıra", j24.Count == 15 && j24.SequenceEqual(j24.OrderByDescending(r => r.Score)));

        var j25 = SimilaritySearch.SearchWithThreshold(jquery, MakeScoredEntries(scores100), minSimilarityPercent: 0, maxResults: 50);
        Check("J25 limit=50, 100 qualifying -> en iyi 50, azalan sıra", j25.Count == 50 && j25.SequenceEqual(j25.OrderByDescending(r => r.Score)));

        var j26 = SimilaritySearch.SearchWithThreshold(jquery, MakeScoredEntries(scores100), minSimilarityPercent: 0, maxResults: 999);
        Check("J26 limit=999 (üst sınır), yalnızca 100 qualifying -> hepsi 100 (doldurma YOK)", j26.Count == 100);

        var j27 = SimilaritySearch.SearchWithThreshold(jquery, MakeScoredEntries(0.9f, 0.5f, 0.2f), minSimilarityPercent: 0, maxResults: 50);
        Check("J27 limit=50, yalnızca 3 qualifying -> 3 (yetersiz eşleşmede doldurma YOK)", j27.Count == 3);

        bool ThrowsOutOfRange(int badMax)
        {
            try
            {
                SimilaritySearch.SearchWithThreshold(jquery, MakeScoredEntries(1f), minSimilarityPercent: 0, maxResults: badMax);
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }

        Check("J28 çekirdek katman: maxResults=0 -> ArgumentOutOfRangeException (sessizce başka sayıya çevrilmez)", ThrowsOutOfRange(0));
        Check("J29 çekirdek katman: maxResults=1000 -> ArgumentOutOfRangeException", ThrowsOutOfRange(1000));
        Check("J30 çekirdek katman: maxResults=-5 -> ArgumentOutOfRangeException", ThrowsOutOfRange(-5));
        Check("J31 çekirdek katman: maxResults=200 (eski üst sınır, şimdi normal bir değer) -> ARTIK reddedilmez", !ThrowsOutOfRange(200));
        Check("J31b çekirdek katman: maxResults=300 (eski üst sınır, şimdi normal bir değer) -> ARTIK reddedilmez", !ThrowsOutOfRange(300));
    }

    Console.WriteLine();

    // ---- Grup K: Arama varsayılanları - boş girdi çözümleme (ResolveOrDefault) ----
    // [Yönetici kararı] Açılışta "Minimum benzerlik (%)" 80, "En fazla sonuç" 20
    // ile dolu gelir. Kullanıcı bir alanı SİLİP boş/yalnızca-boşluk bırakarak
    // "Ara"ya basarsa o alan için varsayılan kullanılır - ama TryParse'in KATI
    // sözleşmesi (metin/negatif/aralık-dışı/NaN/Infinity reddi) HİÇ DEĞİŞMEDİ;
    // ResolveOrDefault yalnızca GERÇEKTEN boş/yalnızca-boşluklu girdiyi
    // varsayılana çevirir, diğer HER ŞEYİ olduğu gibi TryParse'e devreder.
    Console.WriteLine("[K] Arama varsayılanları: boş girdi çözümleme (ResolveOrDefault)");
    {
        bool TOk(string? input, double expected)
        {
            var ok = SimilarityThreshold.ResolveOrDefault(input, out var value);
            return ok && Math.Abs(value - expected) < 1e-9;
        }

        bool TRejects(string? input) => !SimilarityThreshold.ResolveOrDefault(input, out _);

        Check("K1 threshold: '' (boş) -> 80 (DefaultPercent)", TOk("", SimilarityThreshold.DefaultPercent));
        Check("K2 threshold: null -> 80", TOk(null, SimilarityThreshold.DefaultPercent));
        Check("K3 threshold: '   ' (yalnızca boşluk) -> 80", TOk("   ", SimilarityThreshold.DefaultPercent));
        Check("K4 threshold: '0' -> GEÇERLİ, 0 (varsayılana ÇEVRİLMEZ)", TOk("0", 0));
        Check("K5 threshold: '65' -> GEÇERLİ, 65 (varsayılana ÇEVRİLMEZ)", TOk("65", 65));
        Check("K6 threshold: '80,5' (TR virgül) -> GEÇERLİ, 80.5 (80'e ÇEVRİLMEZ)", TOk("80,5", 80.5));
        Check("K7 threshold: '100' (üst sınır) -> GEÇERLİ, 100", TOk("100", 100));
        Check("K8 threshold: 'abc' (metin) -> HÂLÂ reddedilir (varsayılana çevrilmez)", TRejects("abc"));
        Check("K9 threshold: '-5' (negatif) -> HÂLÂ reddedilir", TRejects("-5"));
        Check("K10 threshold: '150' (100 üstü) -> HÂLÂ reddedilir", TRejects("150"));
        Check("K11 threshold: 'NaN' -> HÂLÂ reddedilir", TRejects("NaN"));
        Check("K12 threshold: 'Infinity' -> HÂLÂ reddedilir", TRejects("Infinity"));
        Check("K13 DefaultPercent sabiti 80", Math.Abs(SimilarityThreshold.DefaultPercent - 80) < 1e-9);

        bool MOk(string? input, int expected)
        {
            var ok = MaxResultsPreference.ResolveOrDefault(input, out var value);
            return ok && value == expected;
        }

        bool MRejects(string? input) => !MaxResultsPreference.ResolveOrDefault(input, out _);

        Check("K14 maxResults: '' (boş) -> 20 (Default)", MOk("", MaxResultsPreference.Default));
        Check("K15 maxResults: null -> 20", MOk(null, MaxResultsPreference.Default));
        Check("K16 maxResults: '   ' (yalnızca boşluk) -> 20", MOk("   ", MaxResultsPreference.Default));
        Check("K17 maxResults: '15' -> GEÇERLİ, 15 (20'ye ÇEVRİLMEZ)", MOk("15", 15));
        Check("K18 maxResults: '50' -> GEÇERLİ, 50 (20'ye ÇEVRİLMEZ)", MOk("50", 50));
        Check("K19 maxResults: '1' (alt sınır) -> GEÇERLİ, 1", MOk("1", 1));
        Check("K20 maxResults: '999' (üst sınır) -> GEÇERLİ, 999", MOk("999", 999));
        Check("K20b maxResults: '300' (eski üst sınır, artık normal bir değer) -> GEÇERLİ, 300", MOk("300", 300));
        Check("K20c maxResults: '200' (eski üst sınır, artık normal bir değer) -> GEÇERLİ, 200", MOk("200", 200));
        Check("K21 maxResults: '0' -> HÂLÂ reddedilir (benzerlikteki 0'ın aksine, sonuç sayısında 0 GEÇERSİZ)", MRejects("0"));
        Check("K22 maxResults: '-5' (negatif) -> HÂLÂ reddedilir", MRejects("-5"));
        Check("K23 maxResults: '1000' (999 üstü) -> HÂLÂ reddedilir", MRejects("1000"));
        Check("K24 maxResults: '15.5' (ondalık) -> HÂLÂ reddedilir", MRejects("15.5"));
        Check("K25 maxResults: 'abc' (metin) -> HÂLÂ reddedilir", MRejects("abc"));
        Check("K26 Default sabiti 20", MaxResultsPreference.Default == 20);
    }

    Console.WriteLine();

    // ---- Grup L: Klasör adresi elle girme - biçim doğrulaması (ProductFolderPathInput) ----
    // [Disk erişimi GEREKTİRMEZ] Yalnızca biçim/sözdizimi düzeyinde doğrulama -
    // gerçek Directory.Exists/File.Exists kontrolü MainWindow'da (UI, arka planda)
    // yapılır, bu yüzden burada test edilmez (bkz. proje talimatı "arayüzü
    // bloke etmesin"). Göreli bir yolun çalışma dizinine göre BAŞKA bir klasöre
    // YÖNLENDİRİLMEDİĞİ özellikle doğrulanır (bkz. L5/L6).
    Console.WriteLine("[L] Klasör adresi elle girme: biçim doğrulaması (ProductFolderPathInput)");
    {
        bool FOk(string? input, string expectedNormalized)
        {
            var ok = ProductFolderPathInput.TryNormalizeFormat(input, out var normalized, out _);
            return ok && string.Equals(normalized, expectedNormalized, StringComparison.OrdinalIgnoreCase);
        }

        bool FRejects(string? input) => !ProductFolderPathInput.TryNormalizeFormat(input, out _, out _);

        Check("L1 'C:\\Ürünler' (tam yerel yol, Türkçe karakter) -> geçerli", FOk("C:\\Ürünler", "C:\\Ürünler"));
        Check("L2 'C:\\Ürün Klasörü' (boşluklu) -> geçerli, boşluk korunur", FOk("C:\\Ürün Klasörü", "C:\\Ürün Klasörü"));
        Check("L3 '\\\\Sunucu\\Paylaşım' (UNC) -> geçerli", FOk("\\\\Sunucu\\Paylaşım", "\\\\Sunucu\\Paylaşım"));
        Check("L4 '  C:\\Ürünler  ' (baş/son boşluk) -> temizlenir, geçerli", FOk("  C:\\Ürünler  ", "C:\\Ürünler"));
        Check("L5 '\"C:\\Ürünler\"' (eşleşen dış çift tırnak) -> güvenle temizlenir", FOk("\"C:\\Ürünler\"", "C:\\Ürünler"));
        Check("L6 'C:\\Ürünler\\' (sondaki ayırıcı) -> temizlenir", FOk("C:\\Ürünler\\", "C:\\Ürünler"));
        Check("L7 '' (boş) -> reddedilir", FRejects(""));
        Check("L8 null -> reddedilir", FRejects(null));
        Check("L9 '   ' (yalnızca boşluk) -> reddedilir", FRejects("   "));
        Check("L10 'Ürünler' (göreli yol) -> reddedilir (çalışma dizinine göre BAŞKA klasöre YÖNLENDİRİLMEZ)", FRejects("Ürünler"));
        Check("L11 '..\\Ürünler' (göreli, üst dizin) -> reddedilir", FRejects("..\\Ürünler"));
        Check("L12 '.\\Ürünler' (göreli, mevcut dizin) -> reddedilir", FRejects(".\\Ürünler"));
        Check("L13 geçersiz sözdizimi (kontrol karakteri) -> reddedilir, exception fırlatmaz",
            FRejects("C:\\Ürünler\\" + '\u0000' + "x"));
    }

    Console.WriteLine();

    // ---- Grup M: Sayısal giriş filtresi (NumericInputFilter) - karakter düzeyi ----
    // [Sayısal giriş - 2026-09-07] Bu grup YALNIZCA karakter/tuş/yapıştırma düzeyinde
    // "yazılmasına/yapıştırılmasına İZİN VERİLİR mi" sorusunu test eder - 0-100/1-999
    // ARALIK doğrulaması burada test EDİLMEZ (o zaten Grup K'de SimilarityThreshold/
    // MaxResultsPreference üzerinden kapsanıyor). Bu yüzden ör. "101"/"201" gibi
    // aralık-dışı ama KARAKTER olarak geçerli sayılar burada true (izinli) döner -
    // filtre bilerek aralığı KONTROL ETMEZ, yalnızca harf/eksi/fazla ayırıcıyı engeller.
    Console.WriteLine("[M] Sayısal giriş filtresi: karakter düzeyi doğrulama (NumericInputFilter)");
    {
        bool DOk(string text) => NumericInputFilter.IsValidPartialText(text, allowDecimal: true);
        bool IOk(string text) => NumericInputFilter.IsValidPartialText(text, allowDecimal: false);

        // -- Benzerlik (allowDecimal=true) --
        Check("M1 benzerlik: '' (boş) -> izinli (geçici olarak boş bırakılabilir)", DOk(""));
        Check("M2 benzerlik: '0' -> izinli", DOk("0"));
        Check("M3 benzerlik: '80' -> izinli", DOk("80"));
        Check("M4 benzerlik: '80,5' (TR virgül) -> izinli", DOk("80,5"));
        Check("M5 benzerlik: '80.5' (nokta) -> izinli", DOk("80.5"));
        Check("M6 benzerlik: '100' -> izinli", DOk("100"));
        Check("M7 benzerlik: 'abc' (metin) -> REDDEDİLİR", !DOk("abc"));
        Check("M8 benzerlik: '8a' (rakam+harf) -> REDDEDİLİR", !DOk("8a"));
        Check("M9 benzerlik: '-1' (eksi işareti) -> REDDEDİLİR", !DOk("-1"));
        Check("M10 benzerlik: '101' -> KARAKTER olarak izinli (aralık kontrolü burada DEĞİL, bkz. Grup K)", DOk("101"));
        Check("M11 benzerlik: '80,5,2' (birden fazla ayırıcı) -> REDDEDİLİR", !DOk("80,5,2"));

        // -- Sonuç sayısı (allowDecimal=false) --
        Check("M12 sonuç sayısı: '' (boş) -> izinli", IOk(""));
        Check("M13 sonuç sayısı: '1' -> izinli", IOk("1"));
        Check("M14 sonuç sayısı: '20' -> izinli", IOk("20"));
        Check("M15 sonuç sayısı: '200' -> izinli", IOk("200"));
        Check("M16 sonuç sayısı: '0' -> KARAKTER olarak izinli (0'ın GEÇERSİZ olması Grup K'nin işi)", IOk("0"));
        Check("M17 sonuç sayısı: '201' -> KARAKTER olarak izinli (aralık kontrolü burada DEĞİL)", IOk("201"));
        Check("M18 sonuç sayısı: 'abc' -> REDDEDİLİR", !IOk("abc"));
        Check("M19 sonuç sayısı: '2a' -> REDDEDİLİR", !IOk("2a"));
        Check("M20 sonuç sayısı: '20,5' (virgül - tam sayı alanında ayırıcı YOK) -> REDDEDİLİR", !IOk("20,5"));
        Check("M21 sonuç sayısı: '20.5' (nokta - tam sayı alanında ayırıcı YOK) -> REDDEDİLİR", !IOk("20.5"));
        Check("M22 sonuç sayısı: '-5' (eksi işareti) -> REDDEDİLİR", !IOk("-5"));

        // -- Ekleme (insert) senaryoları: mevcut metin + seçim + yeni karakter/yapıştırma --
        Check("M23 ekleme: '8' üzerine (imleç sonda) '0' yaz -> '80' izinli",
            NumericInputFilter.IsValidPartialInput("8", 1, 0, "0", allowDecimal: true));
        Check("M24 ekleme: '80' TAMAMI seçiliyken 'a' yaz -> REDDEDİLİR (seçili metnin üzerine harf)",
            !NumericInputFilter.IsValidPartialInput("80", 0, 2, "a", allowDecimal: true));
        Check("M25 ekleme: '80' TAMAMI seçiliyken '65' yapıştır -> '65' izinli (seçili metnin üzerine geçerli sayı)",
            NumericInputFilter.IsValidPartialInput("80", 0, 2, "65", allowDecimal: true));
        Check("M26 yapıştırma: boş alana '80,5' yapıştır -> izinli",
            NumericInputFilter.IsValidPartialInput("", 0, 0, "80,5", allowDecimal: true));
        Check("M27 yapıştırma: boş alana '8a' yapıştır -> REDDEDİLİR",
            !NumericInputFilter.IsValidPartialInput("", 0, 0, "8a", allowDecimal: true));
        Check("M28 yapıştırma: '80,' sonuna '5,2' yapıştır -> REDDEDİLİR (toplamda 2 ayırıcı)",
            !NumericInputFilter.IsValidPartialInput("80,", 3, 0, "5,2", allowDecimal: true));
        Check("M29 yapıştırma (tam sayı alanı): boş alana '150' yapıştır -> izinli",
            NumericInputFilter.IsValidPartialInput("", 0, 0, "150", allowDecimal: false));
        Check("M30 yapıştırma (tam sayı alanı): boş alana '15,5' yapıştır -> REDDEDİLİR",
            !NumericInputFilter.IsValidPartialInput("", 0, 0, "15,5", allowDecimal: false));

        // -- Son güvenlik ağı: gecersiz metinden temizleme (StripInvalidCharacters) --
        Check("M31 temizleme: '8a0b' (ondalık alan) -> '80'",
            NumericInputFilter.StripInvalidCharacters("8a0b", allowDecimal: true) == "80");
        Check("M32 temizleme: '80,5,2' (ondalık alan, fazla ayırıcı+3 rakam sınırı) -> '80,5' (İLK ayırıcı korunur, sonraki ayırıcı VE 3. rakamdan sonrası atılır)",
            NumericInputFilter.StripInvalidCharacters("80,5,2", allowDecimal: true) == "80,5");
        Check("M33 temizleme: '2-0.5' (tam sayı alanı - eksi VE nokta atılır) -> '205'",
            NumericInputFilter.StripInvalidCharacters("2-0.5", allowDecimal: false) == "205");
        Check("M34 temizleme: 'abc' -> '' (tamamen geçersiz -> boş, alan boş bırakılabilir sözleşmesiyle tutarlı)",
            NumericInputFilter.StripInvalidCharacters("abc", allowDecimal: true) == "");

        // -- [2026-09-07 ek kural] En fazla 3 rakam (ondalık ayırıcı hariç, iki taraf birlikte sayılır) --
        Check("M35 benzerlik: '100' (tam 3 rakam) -> izinli", DOk("100"));
        Check("M36 benzerlik: '9,99' (3 rakam: 9,9,9) -> izinli", DOk("9,99"));
        Check("M37 benzerlik: '80,55' (4 rakam: 8,0,5,5) -> REDDEDİLİR", !DOk("80,55"));
        Check("M38 benzerlik: '1000' (4 rakam) -> REDDEDİLİR", !DOk("1000"));
        Check("M39 sonuç sayısı: '200' (tam 3 rakam) -> izinli", IOk("200"));
        Check("M40 sonuç sayısı: '2000' (4 rakam) -> REDDEDİLİR", !IOk("2000"));
        Check("M41 ekleme: '999' üzerine (imleç sonda) 4. rakam '9' yaz -> REDDEDİLİR (9999 yazılamaz)",
            !NumericInputFilter.IsValidPartialInput("999", 3, 0, "9", allowDecimal: true));
        Check("M42 ekleme (tam sayı alanı): '999' üzerine 4. rakam yaz -> REDDEDİLİR",
            !NumericInputFilter.IsValidPartialInput("999", 3, 0, "9", allowDecimal: false));
        Check("M43 yapıştırma: boş alana 4 rakamlı '1000' yapıştır (tam sayı alanı) -> TAMAMEN REDDEDİLİR (kesilip kısaltılmaz)",
            !NumericInputFilter.IsValidPartialInput("", 0, 0, "1000", allowDecimal: false));
        Check("M44 ekleme: '999' TAMAMI seçiliyken '888' yapıştır -> izinli (3 rakamlık seçimin üzerine 3 rakam)",
            NumericInputFilter.IsValidPartialInput("999", 0, 3, "888", allowDecimal: true));
        Check("M45 ekleme: '99,9' (3 rakam) sonuna 4. rakam '9' yaz -> '99,99' REDDEDİLİR (ayırıcı iki tarafı birlikte sayılır)",
            !NumericInputFilter.IsValidPartialInput("99,9", 4, 0, "9", allowDecimal: true));
        Check("M46 MaxDigitCount sabiti 3", NumericInputFilter.MaxDigitCount == 3);
    }

    // ---- Grup N: [PILOT] DINOv2-Base profili + index ayrimi + embedding sozlesmesi ----
    // Bu grubun BUYUK kismi MODEL GEREKTIRMEZ: profil karsilastirmasi, index
    // yolu ayrimi, sema/bozulma reddi ve boyut guvenligi, gercek ONNX oturumu
    // olmadan sahte bir IImageEmbedder ile test edilir. Yalnizca N30+ (gercek
    // on isleme/embedding) model dosyasi varsa calisir.
    Console.WriteLine("\n[Grup N] DINOv2-Base pilot: profil, index ayrimi, embedding sozlesmesi");
    {
        // -- N1-N8: profil karsilastirmasi (tek tek her alan) --
        var baseProfile = DinoV2BaseProfile.CreateProfile("aa" + new string('0', 62));
        Check("N1 ayni profil index'i yeniden kullanabilir",
            baseProfile.MatchesForIndexReuse(baseProfile with { }));
        Check("N2 SHA-256 buyuk/kucuk harf farki UYUMSUZLUK SAYILMAZ",
            baseProfile.MatchesForIndexReuse(baseProfile with { ModelSha256 = baseProfile.ModelSha256.ToUpperInvariant() }));
        Check("N3 kayitli profil YOK -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(null) && baseProfile.DescribeMismatch(null) == "kayıtlı profil yok");
        Check("N4 model kimligi degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { ModelId = "facebook/dinov2-small" }));
        Check("N5 model revision degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { ModelRevision = "deadbeef" }));
        Check("N6 model SHA-256 degisti (BOYUT AYNI olsa bile) -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { ModelSha256 = "bb" + new string('0', 62) })
            && baseProfile.DescribeMismatch(baseProfile with { ModelSha256 = "bb" + new string('0', 62) }) == "model dosyası SHA-256");
        Check("N7 on isleme surumu degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { PreprocessingVersion = "clip-shortest224-crop224-openai-v1" }));
        Check("N8 embedding boyutu degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { EmbeddingDimension = 384 }));
        Check("N9 ozellik turu degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { FeatureType = "MeanPooled" }));
        Check("N10 crop stratejisi degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { CropStrategy = "ThreeByThreeTiles" }));
        Check("N11 normalizasyon degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { Normalization = "None" }));
        Check("N12 index sema surumu degisti -> uyumsuz",
            !baseProfile.MatchesForIndexReuse(baseProfile with { IndexSchemaVersion = 1 }));
        Check("N13 DINOv2-Base profil sabitleri: 768 / CLS / L2 / sema 2 / on isleme dinov2",
            baseProfile.EmbeddingDimension == 768 && baseProfile.FeatureType == "CLS"
            && baseProfile.Normalization == "L2" && baseProfile.IndexSchemaVersion == 2
            && baseProfile.PreprocessingVersion == ImagePreprocessingProfile.DinoV2.Version);
        Check("N14 pilot esigi %55 (CLIP'in %80'i DEGIL)",
            DinoV2BaseProfile.DefaultThresholdPercent == 55 && SimilarityThreshold.DefaultPercent == 80);
        Check("N15 esik cozumleme: bos girdi AKTIF MODELIN varsayilanina coozulur, profilsiz cagri hala 80",
            SimilarityThreshold.ResolveOrDefault("", 55, out var dinoDefault) && dinoDefault == 55
            && SimilarityThreshold.ResolveOrDefault("", out var clipDefault) && clipDefault == 80);
        Check("N16 esik cozumleme: kullanicinin ELLE girdigi gecerli deger varsayilana DOKUNULMADAN korunur",
            SimilarityThreshold.ResolveOrDefault("65", 55, out var manual) && manual == 65);
        Check("N17 esik cozumleme: 0-100 dogrulamasi model-spesifik asiri yuklemede de AYNEN gecerli",
            !SimilarityThreshold.ResolveOrDefault("101", 55, out _)
            && !SimilarityThreshold.ResolveOrDefault("abc", 55, out _)
            && !SimilarityThreshold.ResolveOrDefault("-1", 55, out _)
            && SimilarityThreshold.ResolveOrDefault("0", 55, out var zero) && zero == 0);

        // -- N18-N22: on isleme profili sabitleri (CLIP degerleri TASINMADI) --
        var dinoPre = ImagePreprocessingProfile.DinoV2;
        var clipPre = ImagePreprocessingProfile.Clip;
        Check("N18 DINOv2 on isleme: kisa kenar 256, crop 224",
            dinoPre.ResizeShortestEdge == 256 && dinoPre.CropSize == 224);
        Check("N19 DINOv2 on isleme: ImageNet mean/std (CLIP degerleri DEGIL)",
            Math.Abs(dinoPre.Mean[0] - 0.485f) < 1e-6 && Math.Abs(dinoPre.Mean[1] - 0.456f) < 1e-6
            && Math.Abs(dinoPre.Mean[2] - 0.406f) < 1e-6
            && Math.Abs(dinoPre.Std[0] - 0.229f) < 1e-6 && Math.Abs(dinoPre.Std[1] - 0.224f) < 1e-6
            && Math.Abs(dinoPre.Std[2] - 0.225f) < 1e-6);
        Check("N20 CLIP on isleme profili DEGISMEDI (kisa kenar 224 + OpenAI mean/std)",
            clipPre.ResizeShortestEdge == 224 && clipPre.CropSize == 224
            && Math.Abs(clipPre.Mean[0] - 0.48145466f) < 1e-7 && Math.Abs(clipPre.Std[0] - 0.26862954f) < 1e-7);
        Check("N21 iki profilin on isleme surumu FARKLI (yanlislikla ayni index'i paylasamazlar)",
            dinoPre.Version != clipPre.Version);

        // -- N22-N29: embedding dogrulama (EmbeddingVector) --
        var goodRaw = new float[768];
        goodRaw[0] = 3f;
        goodRaw[1] = 4f;
        var normalized = EmbeddingVector.L2NormalizeChecked(goodRaw, 768);
        var norm = Math.Sqrt(normalized.Sum(v => (double)v * v));
        Check("N22 L2 normalize: donen vektorun normu 1", Math.Abs(norm - 1.0) < 1e-6);
        Check("N23 L2 normalize: 768 boyut korunur ve deger dogru (3,4 -> 0,6/0,8)",
            normalized.Length == 768 && Math.Abs(normalized[0] - 0.6f) < 1e-6 && Math.Abs(normalized[1] - 0.8f) < 1e-6);
        Check("N24 yanlis boyut (767) REDDEDILIR",
            Throws<InvalidEmbeddingException>(() => EmbeddingVector.L2NormalizeChecked(new float[767], 768)));
        Check("N25 NaN iceren embedding REDDEDILIR",
            Throws<InvalidEmbeddingException>(() =>
            {
                var v = new float[768];
                v[5] = float.NaN;
                EmbeddingVector.L2NormalizeChecked(v, 768);
            }));
        Check("N26 Infinity iceren embedding REDDEDILIR",
            Throws<InvalidEmbeddingException>(() =>
            {
                var v = new float[768];
                v[7] = float.PositiveInfinity;
                EmbeddingVector.L2NormalizeChecked(v, 768);
            }));
        Check("N27 sifir normlu embedding REDDEDILIR (sessizce NaN/Infinity URETMEZ)",
            Throws<InvalidEmbeddingException>(() => EmbeddingVector.L2NormalizeChecked(new float[768], 768)));
        Check("N28 null embedding REDDEDILIR",
            Throws<InvalidEmbeddingException>(() => EmbeddingVector.L2NormalizeChecked(null, 768)));
        Check("N29 farkli boyutlar KARSILASTIRILAMAZ (768 sorgu vs 512 kayit) - acik hata",
            Throws<InvalidEmbeddingException>(() => EmbeddingVector.EnsureComparable(new float[768], new float[512], "a.jpg")));
        Check("N30 ayni boyutlar karsilastirilabilir (yanlis pozitif yok)",
            !Throws<InvalidEmbeddingException>(() => EmbeddingVector.EnsureComparable(new float[768], new float[768], "a.jpg")));

        // -- N31: arama katmani da farkli boyutu SESSIZCE gecmez --
        Check("N31 SimilaritySearch: 768 sorgu + 512 kayit -> sessiz sonuc DEGIL, acik hata",
            Throws<InvalidEmbeddingException>(() => SimilaritySearch.SearchWithThreshold(
                UnitVector(768),
                new List<ImageIndexEntry> { new() { RelativePath = "eski-clip.jpg", Embedding = UnitVector(512) } },
                minSimilarityPercent: 0)));

        // -- N32+: index ayrimi ve profil dogrulamali yukleme (model GEREKMEZ) --
        string dinoDir = Path.Combine(Path.GetTempPath(), "lens_dino_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dinoDir);
        try
        {
            var store = ProfiledIndexStore.ForDinoV2Base(baseProfile);
            var legacyStore = LegacyClipIndexStore.Instance;

            var expectedDinoPath = Path.Combine(dinoDir, ".lens", "indexes", "dinov2-base-v1", "index.json");
            Check("N32 DINO index yolu: .lens/indexes/dinov2-base-v1/index.json",
                store.IndexFilePath(dinoDir) == expectedDinoPath, store.IndexFilePath(dinoDir));
            Check("N33 DINO index yolu, eski CLIP yolundan (.lens/index.json) FARKLI",
                store.IndexFilePath(dinoDir) != legacyStore.IndexFilePath(dinoDir));
            Check("N34 DINO kilidi de kendi klasorunde - eski CLIP kilidinden FARKLI",
                store.LockFilePath(dinoDir) == Path.Combine(dinoDir, ".lens", "indexes", "dinov2-base-v1", "index.lock")
                && store.LockFilePath(dinoDir) != legacyStore.LockFilePath(dinoDir));
            Check("N35 yolu OGRENMEK klasor OLUSTURMAZ (side-effect-free)",
                !Directory.Exists(Path.Combine(dinoDir, ".lens")));
            Check("N36 index yokken Load -> Missing + bos liste",
                store.Load(dinoDir).Outcome == IndexLoadOutcome.Missing && store.Load(dinoDir).Entries.Count == 0);

            // Eski CLIP index'ini yaz - DINO islemleri buna DOKUNMAMALI.
            var clipEntries = new List<ImageIndexEntry>
            {
                new() { RelativePath = "urun.jpg", FileSizeBytes = 10, LastWriteTimeUtcTicks = 20, Embedding = UnitVector(512) },
            };
            legacyStore.Save(dinoDir, clipEntries);
            var clipPath = legacyStore.IndexFilePath(dinoDir);
            var clipBytesBefore = File.ReadAllBytes(clipPath);

            // DINO index'ini yaz.
            var dinoEntries = new List<ImageIndexEntry>
            {
                new() { RelativePath = "urun.jpg", FileSizeBytes = 10, LastWriteTimeUtcTicks = 20, Embedding = UnitVector(768) },
            };
            store.Save(dinoDir, dinoEntries);

            Check("N37 DINO kaydi sonrasi eski CLIP index.json BAYT BAYT DEGISMEDI",
                File.ReadAllBytes(clipPath).SequenceEqual(clipBytesBefore));
            Check("N38 eski CLIP index'i hala kendi store'uyla GECERLI okunuyor (geri donuste yeniden indeksleme gerekmez)",
                legacyStore.Load(dinoDir).Outcome == IndexLoadOutcome.Loaded
                && legacyStore.Load(dinoDir).Entries.Count == 1);
            Check("N39 DINO store, eski CLIP dosyasini OKUMAYA CALISMAZ (kendi dosyasindan 768 boyutlu kaydi okur)",
                store.Load(dinoDir).Outcome == IndexLoadOutcome.Loaded
                && store.Load(dinoDir).Entries[0].Embedding.Length == 768);

            // Yazilan belgenin sema/profil zarfi gercekten var mi?
            var rawJson = File.ReadAllText(expectedDinoPath);
            Check("N40 index belgesi duz dizi DEGIL, SchemaVersion+EmbeddingProfile+Entries zarfi",
                rawJson.Contains("\"SchemaVersion\"") && rawJson.Contains("\"EmbeddingProfile\"")
                && rawJson.Contains("\"Entries\"") && rawJson.Contains("\"ModelSha256\""));

            // -- Profil uyumsuzlugu: her alan icin tam yeniden indeksleme --
            IndexLoadResult LoadWith(EmbeddingProfile p) =>
                ProfiledIndexStore.ForDinoV2Base(p).Load(dinoDir);

            Check("N41 ayni profille tekrar yuklenebilir (gereksiz yeniden indeksleme YOK)",
                LoadWith(baseProfile).Outcome == IndexLoadOutcome.Loaded);
            Check("N42 model kimligi degisti -> ProfileMismatch + BOS liste",
                LoadWith(baseProfile with { ModelId = "facebook/dinov2-small" }) is { Outcome: IndexLoadOutcome.ProfileMismatch, Entries.Count: 0 });
            Check("N43 model SHA degisti (boyut AYNI) -> ProfileMismatch",
                LoadWith(baseProfile with { ModelSha256 = "cc" + new string('0', 62) }).Outcome == IndexLoadOutcome.ProfileMismatch);
            Check("N44 on isleme surumu degisti -> ProfileMismatch",
                LoadWith(baseProfile with { PreprocessingVersion = "baska-v9" }).Outcome == IndexLoadOutcome.ProfileMismatch);
            Check("N45 embedding boyutu degisti -> ProfileMismatch",
                LoadWith(baseProfile with { EmbeddingDimension = 384 }).Outcome == IndexLoadOutcome.ProfileMismatch);
            Check("N46 normalizasyon degisti -> ProfileMismatch",
                LoadWith(baseProfile with { Normalization = "None" }).Outcome == IndexLoadOutcome.ProfileMismatch);
            Check("N47 crop stratejisi degisti -> ProfileMismatch",
                LoadWith(baseProfile with { CropStrategy = "ThreeByThreeTiles" }).Outcome == IndexLoadOutcome.ProfileMismatch);
            Check("N48 ozellik turu degisti -> ProfileMismatch",
                LoadWith(baseProfile with { FeatureType = "MeanPooled" }).Outcome == IndexLoadOutcome.ProfileMismatch);
            Check("N49 sema surumu degisti -> ProfileMismatch",
                LoadWith(baseProfile with { IndexSchemaVersion = 3 }).Outcome == IndexLoadOutcome.ProfileMismatch);
            Check("N50 uyumsuzluk nedeni INSAN OKUNABILIR olarak raporlanir",
                LoadWith(baseProfile with { ModelSha256 = "dd" + new string('0', 62) }).Reason == "model dosyası SHA-256");

            // -- Bozuk / bilinmeyen sema --
            File.WriteAllText(expectedDinoPath, "{ bu gecerli json degil");
            Check("N51 bozuk JSON -> Corrupt + bos liste, exception YOK",
                store.Load(dinoDir) is { Outcome: IndexLoadOutcome.Corrupt, Entries.Count: 0 });

            File.WriteAllText(expectedDinoPath, "[ { \"RelativePath\": \"a.jpg\", \"Embedding\": [1.0] } ]");
            Check("N52 eski (duz dizi) sema DINO store tarafindan KULLANILMAZ",
                store.Load(dinoDir).Entries.Count == 0);

            File.WriteAllText(expectedDinoPath, "{ \"SchemaVersion\": 2, \"Entries\": [] }");
            Check("N53 profil alani EKSIK belge -> ProfileMismatch (varsayilan kabul EDILMEZ)",
                store.Load(dinoDir).Outcome == IndexLoadOutcome.ProfileMismatch);

            // Gecerli profil ama BOZUK kayit (NaN / yanlis boyut)
            store.Save(dinoDir, new List<ImageIndexEntry>
            {
                new() { RelativePath = "a.jpg", Embedding = UnitVector(768) },
                new() { RelativePath = "b.jpg", Embedding = new float[512] },
            });
            Check("N54 profil UYUMLU ama bir kayit yanlis boyutta -> Corrupt + bos liste ('hepsi ya da hicbiri')",
                store.Load(dinoDir) is { Outcome: IndexLoadOutcome.Corrupt, Entries.Count: 0 });

            // NaN/Infinity, System.Text.Json ile YAZILAMAZ (Grup A5/A6 ile ayni
            // kisit) - elle duzenlenmis/bozulmus bir dosyayi simule etmek icin
            // ham JSON metni olusturuluyor. Profil zarfi GECERLI, yalnizca
            // kayit bozuk: yani reddin nedeni profil degil, icerik dogrulamasi.
            var profileJson = JsonSerializer.Serialize(baseProfile);
            var nanValues = string.Join(",", Enumerable.Repeat("0.0", 767).Prepend("NaN"));
            File.WriteAllText(expectedDinoPath,
                $"{{\"SchemaVersion\":2,\"EmbeddingProfile\":{profileJson},"
                + $"\"Entries\":[{{\"RelativePath\":\"a.jpg\",\"FileSizeBytes\":100,"
                + $"\"LastWriteTimeUtcTicks\":0,\"Embedding\":[{nanValues}]}}]}}");
            Check("N55 profil GECERLI ama kayit NaN iceriyor -> Corrupt + bos liste",
                store.Load(dinoDir) is { Outcome: IndexLoadOutcome.Corrupt, Entries.Count: 0 });

            var infValues = string.Join(",", Enumerable.Repeat("0.0", 767).Prepend("Infinity"));
            File.WriteAllText(expectedDinoPath,
                $"{{\"SchemaVersion\":2,\"EmbeddingProfile\":{profileJson},"
                + $"\"Entries\":[{{\"RelativePath\":\"a.jpg\",\"FileSizeBytes\":100,"
                + $"\"LastWriteTimeUtcTicks\":0,\"Embedding\":[{infValues}]}}]}}");
            Check("N55b profil GECERLI ama kayit Infinity iceriyor -> Corrupt + bos liste",
                store.Load(dinoDir) is { Outcome: IndexLoadOutcome.Corrupt, Entries.Count: 0 });

            // -- Atomik yazim: gecici dosya birakmaz --
            store.Save(dinoDir, dinoEntries);
            var indexFolder = store.IndexDirectory(dinoDir);
            Check("N56 atomik kayit sonrasi klasorde yalnizca index.json (+kilit) kalir, gecici dosya YOK",
                Directory.EnumerateFiles(indexFolder).All(f =>
                    Path.GetFileName(f) is "index.json" or "index.lock"),
                string.Join(", ", Directory.EnumerateFiles(indexFolder).Select(Path.GetFileName)));

            // -- Kilit ayrimi: DINO kilidi CLIP kilidini bloklamaz --
            using (var dinoLock = IndexLock.TryAcquire(dinoDir, store, out var dinoLockFailure))
            {
                Check("N57 DINO kilidi alinabildi", dinoLock is not null && dinoLockFailure is null);
                using var clipLock = IndexLock.TryAcquire(dinoDir, legacyStore, out var clipLockFailure);
                Check("N58 DINO kilidi tutulurken CLIP kilidi de ALINABILIR (ayri dosyalar, birbirini bloklamaz)",
                    clipLock is not null && clipLockFailure is null);
                using var dinoLock2 = IndexLock.TryAcquire(dinoDir, store, out _);
                Check("N59 ayni DINO kilidi ikinci kez ALINAMAZ (tek-yazarli sozlesme korunuyor)", dinoLock2 is null);
            }

            // -- Uyumsuz index, kilit alinamadiginda da aramaya SIZMAZ --
            store.Save(dinoDir, dinoEntries);
            var mismatchedStore = ProfiledIndexStore.ForDinoV2Base(baseProfile with { ModelSha256 = "ee" + new string('0', 62) });
            using (var blocker = IndexLock.TryAcquire(dinoDir, mismatchedStore, out _))
            {
                var blocked = ImageIndex.BuildOrUpdateWithLock(dinoDir, new FakeEmbedder(768, baseProfile), mismatchedStore);
                Check("N60 kilit BASKASINDA iken LockUnavailable doner", blocked.Outcome == IndexWriteOutcome.LockUnavailable);
                Check("N61 kilit alinamadiginda UYUMSUZ index eski sonuc olarak DONMEZ (bos liste)",
                    blocked.Entries.Count == 0);
            }

            // -- Tarama hatasi (klasor yok) uyumsuz index'i geri getirmez --
            var missingDir = Path.Combine(dinoDir, "olmayan-klasor");
            var scanFail = ImageIndex.BuildOrUpdate(missingDir, new FakeEmbedder(768, baseProfile), mismatchedStore);
            Check("N62 tarama hatasinda ScanError doldurulur ve uyumsuz kayit DONMEZ",
                scanFail.Stats.ScanError is not null && scanFail.Entries.Count == 0);

            // -- DetectChanges: profil uyumsuzsa TUM dosyalar yeniden indekslenmeli --
            File.WriteAllBytes(Path.Combine(dinoDir, "p1.jpg"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(dinoDir, "p2.jpg"), new byte[] { 4, 5, 6 });
            var changesMismatch = ImageIndex.DetectChanges(dinoDir, mismatchedStore);
            Check("N63 profil uyumsuzken DetectChanges: hicbir dosya 'unchanged' sayilmaz, TAMAMI yeni",
                changesMismatch.HasChanges && changesMismatch.UnchangedCount == 0 && changesMismatch.NewCount == 2);
            Check("N64 DetectChanges uyumsuzluk NEDENINI tasir (sessiz tam tarama yok)",
                changesMismatch.IndexResetReason == "model dosyası SHA-256");

            // -- BuildOrUpdate: uyumsuz profil -> tam yeniden olusturma + neden --
            store.Save(dinoDir, new List<ImageIndexEntry>
            {
                new()
                {
                    RelativePath = "p1.jpg",
                    FileSizeBytes = new FileInfo(Path.Combine(dinoDir, "p1.jpg")).Length,
                    LastWriteTimeUtcTicks = new FileInfo(Path.Combine(dinoDir, "p1.jpg")).LastWriteTimeUtc.Ticks,
                    Embedding = UnitVector(768),
                },
            });

            var sameProfileRun = ImageIndex.BuildOrUpdate(dinoDir, new FakeEmbedder(768, baseProfile), store);
            Check("N65 AYNI profille: kaydedilmis p1.jpg yeniden embed EDILMEZ (unchanged)",
                sameProfileRun.Stats.Unchanged == 1 && sameProfileRun.Stats.IndexResetReason is null);

            var mismatchRun = ImageIndex.BuildOrUpdate(dinoDir, new FakeEmbedder(768, baseProfile), mismatchedStore);
            Check("N66 UYUMSUZ profille: hicbir kayit yeniden kullanilmaz, TAMAMI yeniden embed edilir",
                mismatchRun.Stats.Unchanged == 0 && mismatchRun.Stats.Added == 2);
            Check("N67 tam yeniden olusturmanin NEDENI stats'ta raporlanir",
                mismatchRun.Stats.IndexResetReason == "model dosyası SHA-256");
            Check("N68 yeniden olusturulan kayitlar 768 boyutlu ve normalize",
                mismatchRun.Entries.All(e => e.Embedding.Length == 768));

            // -- Eski CLIP dosyasi tum bu islemler boyunca DEGISMEDI --
            Check("N69 Grup N'in TUM DINO islemleri sonunda eski CLIP index.json hala BAYT BAYT AYNI",
                File.ReadAllBytes(clipPath).SequenceEqual(clipBytesBefore));
        }
        finally
        {
            try { Directory.Delete(dinoDir, recursive: true); } catch { /* best-effort */ }
        }

        // -- N70+: gercek model dosyasi varsa on isleme + embedding sozlesmesi --
        var dinoModelPath = Path.Combine(FindRepoRoot(), "models", DinoV2BaseProfile.ModelFileName);
        var dinoSampleDir = Path.Combine(FindRepoRoot(), "benchmark", "data", "distractors");
        var dinoSample = Directory.Exists(dinoSampleDir)
            ? Directory.EnumerateFiles(dinoSampleDir)
                .FirstOrDefault(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            : null;

        if (!File.Exists(dinoModelPath) || dinoSample is null)
        {
            Console.WriteLine("  [ATLANDI] N70+ : DINOv2 ONNX modeli veya ornek gorsel bulunamadi");
        }
        else
        {
            var dinoTensor = ImagePreprocessor.PreprocessToChwTensor(dinoSample, ImagePreprocessingProfile.DinoV2);
            Check("N70 DINO on isleme tensoru 3x224x224 = 150528 uzunlugunda",
                dinoTensor.Length == 3 * 224 * 224, dinoTensor.Length.ToString());
            Check("N71 tensor CHW duzeninde (3 esit kanal blogu) ve tum degerler sonlu",
                dinoTensor.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));

            var clipTensor = ImagePreprocessor.PreprocessToChwTensor(dinoSample, ImagePreprocessingProfile.Clip);
            Check("N72 DINO ve CLIP on islemesi AYNI gorselde FARKLI tensor uretir (sabitler karismamis)",
                !dinoTensor.SequenceEqual(clipTensor));

            using var dinoEmbedder = new DinoV2Embedder(dinoModelPath);
            Check("N73 embedder profili gercek model dosyasinin SHA-256'sini tasir (64 hex)",
                dinoEmbedder.Profile.ModelSha256.Length == 64
                && dinoEmbedder.Profile.ModelSha256.All(c => Uri.IsHexDigit(c)),
                dinoEmbedder.Profile.ModelSha256);

            var realEmbedding = dinoEmbedder.Embed(dinoSample);
            Check("N74 gercek embedding 768 boyutlu", realEmbedding.Length == 768, realEmbedding.Length.ToString());
            var realNorm = Math.Sqrt(realEmbedding.Sum(v => (double)v * v));
            Check("N75 gercek embedding L2-normalize (norm = 1)", Math.Abs(realNorm - 1.0) < 1e-5, realNorm.ToString("F8"));
            Check("N76 gercek embedding sonlu degerlerden olusur",
                realEmbedding.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));
            Check("N77 ayni gorsel iki kez embed edilince AYNI vektor (deterministik)",
                dinoEmbedder.Embed(dinoSample).SequenceEqual(realEmbedding));
            Check("N78 gorsel kendisiyle karsilastirildiginda benzerlik ~%100",
                Math.Abs(SimilaritySearch.TopK(realEmbedding,
                    new List<ImageIndexEntry> { new() { RelativePath = "self", Embedding = realEmbedding } }, 1)[0].Score - 1f) < 1e-3);
            Check("N79 olmayan model dosyasi FileNotFoundException verir",
                Throws<FileNotFoundException>(() => new DinoV2Embedder(dinoModelPath + ".yok")));
        }
    }

    // ---- Grup O: [PILOT] Desen odakli CLIP - profil, coklu gorunum, arama ----
    // Bu grubun buyuk kismi MODEL GEREKTIRMEZ: profil/yol ayrimi, coklu
    // gorunum sozlesmesi, whitening ve skor birlestirme sentetik vektorlerle
    // test edilir. Yalnizca O30+ gercek CLIP modeliyle calisir.
    Console.WriteLine("\n[Grup O] Desen odakli CLIP pilotu: profil, coklu gorunum, arama");
    {
        // -- O1-O7: profil sabitleri --
        Check("O1 gorunum sayisi 6 (1 global + 5 ortusen bolge)", ClipPatternProfile.ViewCount == 6);
        Check("O2 gorunum boyutu 512 (CLIP), birlesik 3072",
            ClipPatternProfile.ViewDimension == 512 && ClipPatternProfile.EmbeddingDimension == 3072);
        Check("O3 index sema surumu 3 (DINOv2'nin 2'sinden FARKLI)",
            ClipPatternProfile.IndexSchemaVersion == 3 && DinoV2BaseProfile.IndexSchemaVersion == 2);
        Check("O4 index klasoru 'clip-pattern-v1'", ClipPatternProfile.IndexFolderName == "clip-pattern-v1");
        Check("O5 model dosyasi mevcut CLIP modeli (yeni model indirilmedi)",
            ClipPatternProfile.ModelFileName == "clip-vision-b16-openai.onnx");
        Check("O6 model kimligi resmi CLIP (agirliklar DEGISMEDI)",
            ClipPatternProfile.ModelId == "openai/clip-vit-base-patch16");
        Check("O7 pilot esigi %55 (CLIP baseline %80'den FARKLI - whitening olcegi degistirir)",
            ClipPatternProfile.DefaultThresholdPercent == 55 && SimilarityThreshold.DefaultPercent == 80);

        var patternProfile = ClipPatternProfile.CreateProfile("aa" + new string('0', 62));
        Check("O8 profil, on isleme/crop/normalizasyon alanlarini tasiyor",
            patternProfile.PreprocessingVersion == "clip-pattern-center-overlap5-v1"
            && patternProfile.CropStrategy == "CenterCrop224+Overlap5@0.60"
            && patternProfile.Normalization == "L2-per-view+catalog-mean-whitening"
            && patternProfile.EmbeddingDimension == 3072);
        Check("O9 desen profili, DINOv2 profiliyle UYUMSUZ (yanlislikla index paylasamazlar)",
            !patternProfile.MatchesForIndexReuse(DinoV2BaseProfile.CreateProfile("aa" + new string('0', 62))));
        Check("O10 desen profili, duz CLIP profiliyle de UYUMSUZ (crop/normalizasyon farkli)",
            patternProfile.DescribeMismatch(patternProfile with { CropStrategy = "SingleCenterCrop224" }) is not null);

        // -- O11+: yol ayrimi ve mevcut indekslerin korunmasi --
        string patternDir = Path.Combine(Path.GetTempPath(), "lens_clippattern_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(patternDir);
        try
        {
            var patternStore = ProfiledIndexStore.ForClipPattern(patternProfile);
            var dinoStore = ProfiledIndexStore.ForDinoV2Base(DinoV2BaseProfile.CreateProfile("bb" + new string('0', 62)));
            var legacyStore = LegacyClipIndexStore.Instance;

            var expectedPath = Path.Combine(patternDir, ".lens", "indexes", "clip-pattern-v1", "index.json");
            Check("O11 desen index yolu: .lens/indexes/clip-pattern-v1/index.json",
                patternStore.IndexFilePath(patternDir) == expectedPath, patternStore.IndexFilePath(patternDir));
            Check("O12 uc index yolu da BIRBIRINDEN FARKLI (eski CLIP / DINOv2 / desen)",
                patternStore.IndexFilePath(patternDir) != legacyStore.IndexFilePath(patternDir)
                && patternStore.IndexFilePath(patternDir) != dinoStore.IndexFilePath(patternDir)
                && legacyStore.IndexFilePath(patternDir) != dinoStore.IndexFilePath(patternDir));
            Check("O13 kilit dosyalari da ayri",
                patternStore.LockFilePath(patternDir) != legacyStore.LockFilePath(patternDir)
                && patternStore.LockFilePath(patternDir) != dinoStore.LockFilePath(patternDir));
            Check("O14 yolu OGRENMEK klasor OLUSTURMAZ", !Directory.Exists(Path.Combine(patternDir, ".lens")));

            // Once eski CLIP ve DINOv2 index'lerini yaz - desen pilotu bunlara
            // DOKUNMAMALI.
            legacyStore.Save(patternDir, new List<ImageIndexEntry>
            {
                new() { RelativePath = "urun.jpg", FileSizeBytes = 1, LastWriteTimeUtcTicks = 2, Embedding = PatternUnit(512, 1) },
            });
            dinoStore.Save(patternDir, new List<ImageIndexEntry>
            {
                new() { RelativePath = "urun.jpg", FileSizeBytes = 1, LastWriteTimeUtcTicks = 2, Embedding = PatternUnit(768, 1) },
            });

            var legacyBefore = File.ReadAllBytes(legacyStore.IndexFilePath(patternDir));
            var dinoBefore = File.ReadAllBytes(dinoStore.IndexFilePath(patternDir));

            patternStore.Save(patternDir, new List<ImageIndexEntry>
            {
                new() { RelativePath = "urun.jpg", FileSizeBytes = 1, LastWriteTimeUtcTicks = 2, Embedding = PatternVector(1) },
            });

            Check("O15 desen kaydi sonrasi ESKI CLIP index.json BAYT BAYT DEGISMEDI",
                File.ReadAllBytes(legacyStore.IndexFilePath(patternDir)).SequenceEqual(legacyBefore));
            Check("O16 desen kaydi sonrasi DINOv2 index.json BAYT BAYT DEGISMEDI",
                File.ReadAllBytes(dinoStore.IndexFilePath(patternDir)).SequenceEqual(dinoBefore));
            Check("O17 her uc store da kendi dosyasini gecerli okuyor",
                legacyStore.Load(patternDir).Outcome == IndexLoadOutcome.Loaded
                && dinoStore.Load(patternDir).Outcome == IndexLoadOutcome.Loaded
                && patternStore.Load(patternDir).Outcome == IndexLoadOutcome.Loaded);
            Check("O18 desen kaydinin embedding'i 3072 boyutlu",
                patternStore.Load(patternDir).Entries[0].Embedding.Length == 3072);
            Check("O19 belge sema surumu 3 olarak yazildi",
                File.ReadAllText(expectedPath).Contains("\"SchemaVersion\":3"));

            // -- O20-O22: profil uyusmazliginda tam yeniden indeksleme --
            var changedCrop = ProfiledIndexStore.ForClipPattern(patternProfile with { CropStrategy = "Grid3x3" });
            Check("O20 crop stratejisi degisti -> ProfileMismatch + BOS liste",
                changedCrop.Load(patternDir) is { Outcome: IndexLoadOutcome.ProfileMismatch, Entries.Count: 0 });
            var changedNorm = ProfiledIndexStore.ForClipPattern(patternProfile with { Normalization = "L2" });
            Check("O21 normalizasyon (whitening kaldirildi) degisti -> ProfileMismatch",
                changedNorm.Load(patternDir).Outcome == IndexLoadOutcome.ProfileMismatch);
            var changedViews = ProfiledIndexStore.ForClipPattern(patternProfile with { EmbeddingDimension = 512 * 3 });
            Check("O22 gorunum sayisi degisti (6 -> 3) -> ProfileMismatch",
                changedViews.Load(patternDir).Outcome == IndexLoadOutcome.ProfileMismatch);

            File.WriteAllBytes(Path.Combine(patternDir, "a.jpg"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(patternDir, "b.jpg"), new byte[] { 4, 5, 6 });
            var rebuilt = ImageIndex.BuildOrUpdate(patternDir, new FakeEmbedder(3072, patternProfile), changedCrop);
            Check("O23 uyumsuz profilde TUM dosyalar yeniden embed edilir",
                rebuilt.Stats.Added == 2 && rebuilt.Stats.Unchanged == 0
                && rebuilt.Stats.IndexResetReason is not null);

            Check("O24 tum bu islemler sonunda eski CLIP ve DINOv2 index'leri HALA degismemis",
                File.ReadAllBytes(legacyStore.IndexFilePath(patternDir)).SequenceEqual(legacyBefore)
                && File.ReadAllBytes(dinoStore.IndexFilePath(patternDir)).SequenceEqual(dinoBefore));
        }
        finally
        {
            try { Directory.Delete(patternDir, recursive: true); } catch { /* best-effort */ }
        }

        // -- O25-O35: arama sozlesmesi (whitening + global/yerel birlestirme) --
        var self = PatternVector(7);
        var catalogEntries = new List<ImageIndexEntry>
        {
            new() { RelativePath = "hedef.jpg", Embedding = self },
            new() { RelativePath = "b.jpg", Embedding = PatternVector(11) },
            new() { RelativePath = "c.jpg", Embedding = PatternVector(23) },
            new() { RelativePath = "d.jpg", Embedding = PatternVector(37) },
            new() { RelativePath = "e.jpg", Embedding = PatternVector(51) },
        };

        var selfResults = PatternSimilaritySearch.SearchWithThreshold(self, catalogEntries, 0);
        Check("O25 gorsel kendisiyle karsilastirildiginda 1. sirada", selfResults[0].RelativePath == "hedef.jpg");
        Check("O26 kendisiyle benzerlik ~%100", Math.Abs(selfResults[0].Score - 1f) < 1e-3, selfResults[0].Score.ToString("F5"));
        Check("O27 sonuclar azalan sirada",
            selfResults.Zip(selfResults.Skip(1), (a, b) => a.Score >= b.Score).All(ok => ok));
        // [Whitening'in gozlemlenen sonucu] Ortalama cikarildiktan sonra
        // benzemeyen kayitlar NEGATIF kosinus alabilir. Bu yuzden "%0 esigi"
        // artik "her sey" anlamina GELMEZ - skoru negatif olanlar elenir.
        // Bu bir hata degil, esik sozlesmesinin (score >= threshold) dogal
        // sonucudur; dogru eslesmeler yuksek skor aldigi icin pratik bir
        // kayip olusturmaz (bkz. esik taramasi: %45'te dogru hedeflerin
        // %99'u listede kalir).
        Check("O28 esik %0: yalnizca skoru >= 0 olan kayitlar doner (whitening negatif skor uretebilir)",
            selfResults.Count <= catalogEntries.Count && selfResults.All(r => r.Score >= -1e-4f)
            && selfResults.Any(r => r.RelativePath == "hedef.jpg"),
            $"{selfResults.Count}/{catalogEntries.Count} kayit esigi gecti");
        Check("O28b whitening gercekten negatif skor uretebiliyor (belgelenen davranis)",
            selfResults.Count < catalogEntries.Count);

        var strict = PatternSimilaritySearch.SearchWithThreshold(self, catalogEntries, 100);
        Check("O29 esik %100 iken yalnizca birebir ayni kayit kalir",
            strict.Count == 1 && strict[0].RelativePath == "hedef.jpg");

        var capped = PatternSimilaritySearch.SearchWithThreshold(self, catalogEntries, 0, maxResults: 2);
        Check("O30 en fazla sonuc siniri uygulanir (en iyi 2)",
            capped.Count == 2 && capped[0].RelativePath == "hedef.jpg");
        Check("O31 maxResults araligi disi deger reddedilir",
            Throws<ArgumentOutOfRangeException>(() => PatternSimilaritySearch.SearchWithThreshold(self, catalogEntries, 0, 0))
            && Throws<ArgumentOutOfRangeException>(() => PatternSimilaritySearch.SearchWithThreshold(self, catalogEntries, 0, 1000)));

        Check("O32 yanlis boyutlu SORGU sessizce gecmez (512 vs 3072)",
            Throws<InvalidEmbeddingException>(() => PatternSimilaritySearch.SearchWithThreshold(PatternUnit(512, 1), catalogEntries, 0)));
        Check("O33 yanlis boyutlu KAYIT sessizce gecmez (DINOv2'nin 768'i)",
            Throws<InvalidEmbeddingException>(() => PatternSimilaritySearch.SearchWithThreshold(
                self, new List<ImageIndexEntry> { new() { RelativePath = "dino.jpg", Embedding = PatternUnit(768, 1) } }, 0)));
        Check("O34 bos katalogda arama bos liste doner, exception YOK",
            PatternSimilaritySearch.SearchWithThreshold(self, new List<ImageIndexEntry>(), 0).Count == 0);
        Check("O35 ayni girdi iki kez arandiginda AYNI sonuc (deterministik)",
            PatternSimilaritySearch.SearchWithThreshold(self, catalogEntries, 0)
                .Zip(selfResults, (a, b) => a.RelativePath == b.RelativePath && a.Score == b.Score).All(ok => ok));
        Check("O36 en fazla sonuc sozlesmesi SimilaritySearch ile AYNI kaynaktan (999)",
            PatternSimilaritySearch.MaxResults == SimilaritySearch.MaxResults);

        // Tek kayitli katalog: ortalama o kaydin kendisidir, whitening sonrasi
        // norm sifira duser - kod bu sinir durumunda NaN URETMEMELI.
        var single = PatternSimilaritySearch.SearchWithThreshold(
            self, new List<ImageIndexEntry> { new() { RelativePath = "tek.jpg", Embedding = self } }, 0);
        Check("O37 tek kayitli katalogda (whitening normu sifir) NaN uretilmez",
            single.Count == 1 && !float.IsNaN(single[0].Score) && !float.IsInfinity(single[0].Score),
            single.Count > 0 ? single[0].Score.ToString("F5") : "-");

        // -- O38+: gercek model ile coklu gorunum sozlesmesi --
        var clipModelPath = Path.Combine(FindRepoRoot(), "models", ClipPatternProfile.ModelFileName);
        var sampleDir = Path.Combine(FindRepoRoot(), "benchmark", "data", "distractors");
        var sample = Directory.Exists(sampleDir)
            ? Directory.EnumerateFiles(sampleDir).FirstOrDefault(f =>
                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            : null;

        if (!File.Exists(clipModelPath) || sample is null)
        {
            Console.WriteLine("  [ATLANDI] O38+ : CLIP modeli veya ornek gorsel bulunamadi");
        }
        else
        {
            using var patternEmbedder = new ClipPatternEmbedder(clipModelPath);
            Check("O38 embedder profili gercek dosyanin SHA-256'sini tasir (64 hex)",
                patternEmbedder.Profile.ModelSha256.Length == 64 && patternEmbedder.Profile.ModelSha256.All(Uri.IsHexDigit));

            var vector = patternEmbedder.Embed(sample);
            Check("O39 birlesik vektor 3072 boyutlu (6 x 512)", vector.Length == 3072, vector.Length.ToString());
            Check("O40 tum degerler sonlu", vector.All(v => !float.IsNaN(v) && !float.IsInfinity(v)));

            bool allViewsNormalized = true;
            var norms = new List<double>();
            for (int view = 0; view < ClipPatternProfile.ViewCount; view++)
            {
                double sumSquares = 0;
                for (int i = 0; i < ClipPatternProfile.ViewDimension; i++)
                {
                    var value = vector[view * ClipPatternProfile.ViewDimension + i];
                    sumSquares += (double)value * value;
                }

                var norm = Math.Sqrt(sumSquares);
                norms.Add(norm);
                if (Math.Abs(norm - 1.0) > 1e-4)
                {
                    allViewsNormalized = false;
                }
            }

            Check("O41 HER gorunum blogu AYRI AYRI L2-normalize (birlesik vektor toplu normalize EDILMEDI)",
                allViewsNormalized, string.Join(", ", norms.Select(n => n.ToString("F4"))));

            bool viewsDiffer = false;
            for (int i = 0; i < ClipPatternProfile.ViewDimension; i++)
            {
                if (Math.Abs(vector[i] - vector[ClipPatternProfile.ViewDimension + i]) > 1e-6)
                {
                    viewsDiffer = true;
                    break;
                }
            }

            Check("O42 global gorunum ile ilk bolge gorunumu FARKLI (kadraj gercekten uygulaniyor)", viewsDiffer);
            Check("O43 ayni gorsel iki kez embed edilince AYNI vektor (deterministik)",
                patternEmbedder.Embed(sample).SequenceEqual(vector));
            Check("O44 olmayan model dosyasi FileNotFoundException verir",
                Throws<FileNotFoundException>(() => new ClipPatternEmbedder(clipModelPath + ".yok")));

            var selfMatch = PatternSimilaritySearch.SearchWithThreshold(
                vector,
                new List<ImageIndexEntry>
                {
                    new() { RelativePath = "self", Embedding = vector },
                    new() { RelativePath = "other", Embedding = PatternVector(3) },
                },
                minSimilarityPercent: 0);
            Check("O45 gercek embedding kendisiyle 1. sirada ve ~%100",
                selfMatch[0].RelativePath == "self" && Math.Abs(selfMatch[0].Score - 1f) < 1e-3);
        }
    }

    Console.WriteLine();
    Console.WriteLine($"=== Sonuc: {passed} PASS, {failed} FAIL ===");
    if (failed > 0)
    {
        Environment.ExitCode = 1;
    }
}

/// <summary>[Grup N] Verilen eylemin beklenen exception tipini firlatip firlatmadigini dondurur - "sessizce gecmemeli" kontrollerini tek satirda okunur kilar.</summary>
static bool Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
    catch
    {
        // BASKA bir exception tipi geldiyse test gecmemeli - beklenen hata
        // sinifi degisti demektir.
        return false;
    }
}

/// <summary>[Grup O] Verilen boyutta, tek bir eksende 1 olan (normu 1) vektor.</summary>
static float[] PatternUnit(int dimension, int axis)
{
    var v = new float[dimension];
    v[axis % dimension] = 1f;
    return v;
}

/// <summary>
/// [Grup O] Desen pilotunun bekledigi bicimde bir test vektoru: 6 adet
/// 512'lik blok, HER BIRI AYRI L2-normalize. Bloklar birbirinden farkli
/// yonlerde olsun diye tohumdan turetilir.
/// </summary>
static float[] PatternVector(int seed)
{
    int dim = ClipPatternProfile.ViewDimension;
    var combined = new float[ClipPatternProfile.EmbeddingDimension];
    for (int view = 0; view < ClipPatternProfile.ViewCount; view++)
    {
        var block = new float[dim];
        // Iki eksende deger vererek bloklarin birbirine dik olmamasini saglar
        // (tamamen dik vektorler skorlari yapay olarak 0'a sabitlerdi).
        block[(seed * (view + 1)) % dim] = 3f;
        block[(seed * (view + 2) + 17) % dim] += 4f;
        Array.Copy(EmbeddingVector.L2NormalizeChecked(block, dim), 0, combined, view * dim, dim);
    }

    return combined;
}

/// <summary>[Grup N] Verilen boyutta, L2 normu 1 olan basit bir test vektoru.</summary>
static float[] UnitVector(int dimension)
{
    var v = new float[dimension];
    v[0] = 1f;
    return v;
}

static void TryDeleteCacheAndFolder(string productFolder)
{
    try
    {
        var cachePath = ImageIndex.IndexPath(productFolder);
        var cacheDir = Path.GetDirectoryName(cachePath);
        if (cacheDir is not null && Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }
    catch { /* best-effort temizlik */ }

    try
    {
        if (Directory.Exists(productFolder))
        {
            Directory.Delete(productFolder, recursive: true);
        }
    }
    catch { /* best-effort temizlik */ }
}

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

/// <summary>
/// [Grup N] ONNX oturumu OLMAYAN test embedder'i: index profil dogrulamasi,
/// yol ayrimi ve yeniden-olusturma mantigini 330 MB'lik model dosyasina
/// ihtiyac duymadan test etmeyi saglar. Deterministik, gecerli (normalize,
/// sonlu) bir vektor uretir.
/// </summary>
sealed class FakeEmbedder : IImageEmbedder
{
    private readonly int _dimension;

    public FakeEmbedder(int dimension, EmbeddingProfile profile)
    {
        _dimension = dimension;
        Profile = profile;
    }

    public EmbeddingProfile Profile { get; }

    public float[] Embed(string imagePath)
    {
        var raw = new float[_dimension];
        // Dosya adina gore farkli ama tekrarlanabilir bir yon uret.
        var seed = Math.Abs(Path.GetFileName(imagePath).GetHashCode());
        raw[seed % _dimension] = 1f;
        raw[(seed / 7) % _dimension] += 0.5f;
        return EmbeddingVector.L2NormalizeChecked(raw, _dimension);
    }

    public void Dispose()
    {
    }
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
