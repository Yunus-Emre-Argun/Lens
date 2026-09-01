using System.Text.Json;
using Lens.Core.Ai;
using Lens.Core.Config;
using Lens.Core.IO;

namespace Lens.Core.Indexing;

/// <summary>
/// Klasor bazli, kalici, JSON dosyasina yazilan basit embedding index'i.
/// Vector DB / SQLite kullanilmiyor (bkz. docs/DECISIONS.md #23) - MVP
/// olcegi (~10-5000 gorsel) icin duz dosya yeterli.
///
/// [Faz 4A] Index dosyasi artik urun klasorunde DEGIL,
/// %LocalAppData%\Lens\cache\&lt;path-hash&gt;\index.json altinda tutulur
/// (bkz. docs/DECISIONS.md #39, #44) - paylasilan ag klasoru birden fazla
/// kullanicinin ayni dosyaya yazmasindan (race condition/bozulma riski)
/// ve Lens'e ozel teknik dosyalarla kirlenmekten korunur. Yazim atomic'tir
/// (bkz. docs/DECISIONS.md #40, AtomicFileWriter).
///
/// [Faz 4B] Dosya siniflandirmasi FileClassifier'a devredildi (bkz.
/// docs/DECISIONS.md #37) ve ucuz bir "degisiklik var mi?" kontrolu
/// (DetectChanges) eklendi - search-before-refresh bunu kullanir.
/// </summary>
public static class ImageIndex
{
    public static string IndexPath(string folderPath) => AppPaths.CacheIndexFilePath(folderPath);

    public static List<ImageIndexEntry> Load(string folderPath)
    {
        var path = IndexPath(folderPath);
        if (!File.Exists(path))
        {
            return new List<ImageIndexEntry>();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ImageIndexEntry>>(json) ?? new List<ImageIndexEntry>();
    }

    public static void Save(string folderPath, List<ImageIndexEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries);
        AtomicFileWriter.WriteAllText(IndexPath(folderPath), json);
    }

    /// <summary>
    /// Klasoru tarar; degismeyen dosyalar icin var olan embedding'i yeniden
    /// kullanir, yeni/degismis dosyalar icin embedder ile yeniden hesaplar,
    /// artik var olmayan dosyalari cikarir. Okunamayan/bozuk bir gorsel tum
    /// islemi durdurmaz; SupportedImageButFailed olarak Issues'a eklenir.
    /// Taramanin KENDISI basarisiz olursa (orn. UNC yola hic ulasilamadi),
    /// eski index DEGISTIRILMEDEN doner - hicbir sey kaybedilmez/bozulmaz.
    /// </summary>
    public static (List<ImageIndexEntry> Entries, IndexUpdateStats Stats) BuildOrUpdate(
        string folderPath, ClipEmbedder embedder, IProgress<(int Done, int Total)>? progress = null)
    {
        var stats = new IndexUpdateStats();
        var existingEntries = Load(folderPath);

        List<string> supportedFiles;
        try
        {
            var classification = ClassifyDirectory(folderPath);
            supportedFiles = classification.SupportedFiles;
            stats.TotalFilesScanned = classification.TotalFiles;
            stats.SupportedImagesSeen = supportedFiles.Count;
            stats.UnsupportedFormatCount = classification.UnsupportedFormatCount;
            stats.SkippedNonImageCount = classification.SkippedNonImageCount;
            stats.Issues.AddRange(classification.UnsupportedIssues);
        }
        catch (Exception ex)
        {
            stats.ScanError = ex.Message;
            return (existingEntries, stats);
        }

        var existingByPath = existingEntries.ToDictionary(e => e.RelativePath, e => e);
        var result = new List<ImageIndexEntry>();

        int done = 0;
        foreach (var filePath in supportedFiles)
        {
            var relativePath = Path.GetFileName(filePath);
            var extension = Path.GetExtension(filePath);

            try
            {
                var fileInfo = new FileInfo(filePath);
                var sizeBytes = fileInfo.Length;
                var lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;

                var isUnchanged = existingByPath.TryGetValue(relativePath, out var existingEntry)
                    && existingEntry!.FileSizeBytes == sizeBytes
                    && existingEntry.LastWriteTimeUtcTicks == lastWriteTicks;

                if (isUnchanged)
                {
                    result.Add(existingEntry!);
                    stats.Unchanged++;
                }
                else
                {
                    var embedding = embedder.Embed(filePath);
                    result.Add(new ImageIndexEntry
                    {
                        RelativePath = relativePath,
                        FileSizeBytes = sizeBytes,
                        LastWriteTimeUtcTicks = lastWriteTicks,
                        Embedding = embedding,
                    });

                    if (existingEntry is null)
                    {
                        stats.Added++;
                    }
                    else
                    {
                        stats.Updated++;
                    }
                }
            }
            catch (Exception ex)
            {
                // Dosya metadata okuma VEYA embed etme sirasinda basarisiz oldu
                // (bozuk dosya, ani network kopmasi vb.) - bu TEK dosyayi
                // atlar, taramanin geri kalanini durdurmaz.
                stats.Issues.Add(new IndexFileIssue(relativePath, extension, FileIssueKind.SupportedImageButFailed, ex.Message));
            }

            done++;
            progress?.Report((done, supportedFiles.Count));
        }

        var currentPaths = new HashSet<string>(result.Select(e => e.RelativePath));
        stats.Removed = existingByPath.Keys.Count(k => !currentPaths.Contains(k));

        return (result, stats);
    }

    /// <summary>
    /// [Faz 4B] Search-before-refresh icin ucuz bir "degisiklik var mi?"
    /// kontrolu: hicbir gorsel embed edilmez, yalnizca dosya listesi +
    /// boyut/LastWriteTimeUtc mevcut index ile karsilastirilir. UNC uzerinde
    /// bile hizlidir (agir olan CLIP inference'i, network I/O degil).
    /// </summary>
    public static ChangeSummary DetectChanges(string folderPath)
    {
        List<string> supportedFiles;
        try
        {
            supportedFiles = ClassifyDirectory(folderPath).SupportedFiles;
        }
        catch (Exception ex)
        {
            return new ChangeSummary(0, 0, 0, 0, ex.Message);
        }

        var existingByPath = Load(folderPath).ToDictionary(e => e.RelativePath, e => e);
        int newCount = 0;
        int changedCount = 0;
        int unchangedCount = 0;
        var seenPaths = new HashSet<string>();

        foreach (var filePath in supportedFiles)
        {
            var relativePath = Path.GetFileName(filePath);
            seenPaths.Add(relativePath);

            try
            {
                var fileInfo = new FileInfo(filePath);
                var sizeBytes = fileInfo.Length;
                var lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;

                if (!existingByPath.TryGetValue(relativePath, out var existingEntry))
                {
                    newCount++;
                }
                else if (existingEntry.FileSizeBytes != sizeBytes || existingEntry.LastWriteTimeUtcTicks != lastWriteTicks)
                {
                    changedCount++;
                }
                else
                {
                    unchangedCount++;
                }
            }
            catch
            {
                // Tekil dosyanin metadata'sina erisilemedi (orn. anlik network
                // sorunu) - temkinli davran: "degismis" say ki gercek
                // BuildOrUpdate calisip durumu netlestirsin. Sessizce yok
                // saymak gercek bir degisikligi kacirabilir.
                changedCount++;
            }
        }

        var removedCount = existingByPath.Keys.Count(k => !seenPaths.Contains(k));
        return new ChangeSummary(newCount, changedCount, removedCount, unchangedCount, ScanError: null);
    }

    private static DirectoryClassification ClassifyDirectory(string folderPath)
    {
        var allFiles = Directory.EnumerateFiles(folderPath).ToList();
        var supported = new List<string>();
        var unsupportedIssues = new List<IndexFileIssue>();
        int unsupportedCount = 0;
        int skippedCount = 0;

        foreach (var filePath in allFiles)
        {
            var extension = Path.GetExtension(filePath);
            switch (FileClassifier.Classify(extension))
            {
                case FileClassification.SupportedImage:
                    supported.Add(filePath);
                    break;
                case FileClassification.UnsupportedImageFormat:
                    unsupportedCount++;
                    unsupportedIssues.Add(new IndexFileIssue(
                        Path.GetFileName(filePath), extension, FileIssueKind.UnsupportedImageFormat,
                        "Bilinen görsel formatı ama şu an desteklenmiyor"));
                    break;
                default:
                    skippedCount++;
                    break;
            }
        }

        supported.Sort(StringComparer.OrdinalIgnoreCase);
        return new DirectoryClassification(supported, allFiles.Count, unsupportedCount, skippedCount, unsupportedIssues);
    }

    private sealed record DirectoryClassification(
        List<string> SupportedFiles,
        int TotalFiles,
        int UnsupportedFormatCount,
        int SkippedNonImageCount,
        List<IndexFileIssue> UnsupportedIssues);
}
