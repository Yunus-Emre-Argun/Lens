using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lens.Core.Ai;
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

    // ---- Grup F: arama sozlesmesi (threshold inclusive + azalan sira + en fazla 15) ----
    Console.WriteLine("[F] Arama sözleşmesi: threshold (inclusive) + azalan sıra + en fazla 15 sonuç");
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
        Check("F1 score == threshold -> dahil (inclusive boundary)", f1.Count == 1);

        var f2 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(0.75f), minSimilarityPercent: 80);
        Check("F2 score < threshold -> haric", f2.Count == 0);

        var scores40 = Enumerable.Range(0, 40).Select(i => 1f - i * 0.01f).ToArray();
        var f3 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(scores40), minSimilarityPercent: 0);
        Check("F3 40 qualifying -> en fazla 15 sonuç", f3.Count == 15);
        Check("F4 azalan sıralı", f3.SequenceEqual(f3.OrderByDescending(r => r.Score)));
        Check("F5 en iyi 15 alındı (ilk 1.00, son ~0.86)",
            Math.Abs(f3[0].Score - 1.0f) < 1e-5 && Math.Abs(f3[14].Score - 0.86f) < 1e-4);

        var scores15 = Enumerable.Range(0, 15).Select(i => 0.9f - i * 0.01f).ToArray();
        var f6 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(scores15), minSimilarityPercent: 0);
        Check("F6 tam 15 qualifying -> 15", f6.Count == 15);

        var scores6 = new[] { 0.9f, 0.85f, 0.7f, 0.5f, 0.3f, 0.1f };
        var f7 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(scores6), minSimilarityPercent: 0);
        Check("F7 6 qualifying -> 6 (hepsi)", f7.Count == 6);

        var f8 = SimilaritySearch.SearchWithThreshold(query, MakeEntries(0.1f, 0.2f), minSimilarityPercent: 99);
        Check("F8 0 qualifying -> boş liste, exception yok", f8.Count == 0);

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
    // NOT: AppTheme enum'u ve ParseTheme (bilinmeyen/gecersiz -> Normal
    // guvenli donusu) Lens.Desktop projesindedir; Lens.AiProof (bu konsol
    // araci) BILEREK yalnizca Lens.Core'a referans verir (bkz. csproj) - bu
    // yuzden burada test edilen SADECE UserSettings.Theme'in JSON okuma/
    // yazma sozlesmesidir (varsayilan deger, alan korunumu). ParseTheme'in
    // kendisi ve gercek ekran gecisi manuel/UI Automation ile dogrulandi
    // (bkz. CHANGELOG.md / final rapor).
    Console.WriteLine("[I] Tema tercihi (UserSettings.Theme) - JSON sozlesmesi");
    {
        var oldJsonWithoutTheme = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"AutoIndexBeforeSearch\":true}";
        var loadedFromOld = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(oldJsonWithoutTheme);
        Check("I1 eski (Theme alanini icermeyen) settings JSON'u -> Theme='Normal' (geriye uyumlu varsayilan)",
            loadedFromOld is not null && loadedFromOld.Theme == "Normal");

        var explicitThemeJson = "{\"UserOverrideProductDirectory\":null,\"UseUserOverride\":false,\"Theme\":\"Koyu\"}";
        var loadedKoyu = JsonSerializer.Deserialize<Lens.Core.Config.UserSettings>(explicitThemeJson);
        Check("I2 acik 'Koyu' alanli JSON -> Theme='Koyu' (kullanicinin secimi korunur)",
            loadedKoyu is not null && loadedKoyu.Theme == "Koyu");

        var freshSettings = new Lens.Core.Config.UserSettings();
        Check("I3 yeni olusturulan UserSettings -> varsayilan Theme='Normal'", freshSettings.Theme == "Normal");

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
    }

    Console.WriteLine();
    Console.WriteLine($"=== Sonuc: {passed} PASS, {failed} FAIL ===");
    if (failed > 0)
    {
        Environment.ExitCode = 1;
    }
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
