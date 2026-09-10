using System.Text.Json;
using Lens.Core.Ai;
using Lens.Core.Config;
using Lens.Core.IO;
using Lens.Core.Logging;

namespace Lens.Core.Indexing;

/// <summary>
/// [ESKI BICIM - DEGISTIRILMEZ] CLIP'in profilsiz index'i:
/// <c>&lt;ProductDirectory&gt;/.lens/index.json</c>, koke duz bir
/// <see cref="ImageIndexEntry"/> dizisi, 512 boyutlu embedding.
///
/// Bu store, profil mimarisi eklenmeden ONCEKI <c>ImageIndex.Load/Save</c>
/// davranisini BIREBIR korur - dosya bicimi de degismez, boylece kullanici
/// CLIP surumune donerse mevcut index'i AYNEN gecerli kalir ve yeniden
/// indekslemek zorunda kalmaz.
///
/// DINOv2 pilotu bu dosyayi OKUMAZ, YAZMAZ, SILMEZ (bkz.
/// <see cref="ProfiledIndexStore"/>).
/// </summary>
public sealed class LegacyClipIndexStore : IIndexStore
{
    /// <summary>Durumsuz oldugu icin tek ortak ornek - her cagrida yeni nesne olusturmaya gerek yok.</summary>
    public static readonly LegacyClipIndexStore Instance = new();

    /// <inheritdoc />
    public string Description => "CLIP (eski profilsiz index)";

    /// <inheritdoc />
    public int ExpectedEmbeddingDimension => ClipEmbedder.EmbeddingDimension;

    /// <inheritdoc />
    public string IndexDirectory(string productDirectory) => AppPaths.SharedIndexDirectory(productDirectory);

    /// <inheritdoc />
    public string IndexFilePath(string productDirectory) => AppPaths.SharedIndexFilePath(productDirectory);

    /// <inheritdoc />
    public string LockFilePath(string productDirectory) => AppPaths.SharedIndexLockFilePath(productDirectory);

    /// <inheritdoc />
    public IndexLoadResult Load(string productDirectory, ILensLogger? logger = null)
    {
        var path = IndexFilePath(productDirectory);
        if (!File.Exists(path))
        {
            return new IndexLoadResult(IndexLoadOutcome.Missing, new List<ImageIndexEntry>(), null);
        }

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<ImageIndexEntry>>(json);
            if (entries is null || entries.Any(e => !IndexEntryValidation.IsValid(e, ExpectedEmbeddingDimension)))
            {
                logger?.Warning("IndexCacheLoad", file: path,
                    reason: "Cache içeriği geçersiz veya uyumsuz - yok sayıldı, yeniden oluşturulacak");
                return new IndexLoadResult(IndexLoadOutcome.Corrupt, new List<ImageIndexEntry>(),
                    "Cache içeriği geçersiz veya uyumsuz");
            }

            return new IndexLoadResult(IndexLoadOutcome.Loaded, entries, null);
        }
        catch (Exception ex)
        {
            // Bozuk/yarim JSON (orn. yazim sirasinda kesinti) - dosyaya
            // dokunulmaz (bir sonraki basarili Save zaten atomic overwrite
            // yapar), sadece bu yuklemede yok sayilir.
            logger?.Error("IndexCacheLoad", file: path, reason: ex.Message);
            return new IndexLoadResult(IndexLoadOutcome.Corrupt, new List<ImageIndexEntry>(), ex.Message);
        }
    }

    /// <inheritdoc />
    public void Save(string productDirectory, List<ImageIndexEntry> entries)
    {
        Directory.CreateDirectory(IndexDirectory(productDirectory));
        var json = JsonSerializer.Serialize(entries);
        AtomicFileWriter.WriteAllText(IndexFilePath(productDirectory), json);
    }
}
