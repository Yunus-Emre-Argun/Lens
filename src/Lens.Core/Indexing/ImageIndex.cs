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
/// </summary>
public static class ImageIndex
{
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp",
    };

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
    /// artik var olmayan dosyalari cikarir. Okunamayan bir gorsel tum islemi
    /// durdurmaz; atlanir ve IndexUpdateStats.Errors'a eklenir.
    /// </summary>
    public static (List<ImageIndexEntry> Entries, IndexUpdateStats Stats) BuildOrUpdate(
        string folderPath, ClipEmbedder embedder, IProgress<(int Done, int Total)>? progress = null)
    {
        var existingByPath = Load(folderPath).ToDictionary(e => e.RelativePath, e => e);
        var stats = new IndexUpdateStats();
        var result = new List<ImageIndexEntry>();

        var files = Directory.EnumerateFiles(folderPath)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int done = 0;
        foreach (var filePath in files)
        {
            var relativePath = Path.GetFileName(filePath);
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
                try
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
                catch (Exception ex)
                {
                    stats.Errors.Add($"{relativePath}: {ex.Message}");
                }
            }

            done++;
            progress?.Report((done, files.Count));
        }

        var currentPaths = new HashSet<string>(result.Select(e => e.RelativePath));
        stats.Removed = existingByPath.Keys.Count(k => !currentPaths.Contains(k));

        return (result, stats);
    }
}
