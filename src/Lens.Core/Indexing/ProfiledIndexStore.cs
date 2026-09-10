using System.Text.Json;
using Lens.Core.Ai;
using Lens.Core.Config;
using Lens.Core.IO;
using Lens.Core.Logging;

namespace Lens.Core.Indexing;

/// <summary>
/// Model profiline OZEL index deposu:
/// <c>&lt;ProductDirectory&gt;/.lens/indexes/&lt;profil-klasoru&gt;/index.json</c>
/// (kilit dosyasi da AYNI klasorde). Iki temel garantisi vardir:
///
/// 1. <b>Yol ayrimi:</b> eski CLIP dosyasi (`.lens/index.json`) ile ayni
///    dosyayi hicbir kosulda paylasmaz; farkli profiller de birbirinin
///    uzerine yazmaz.
/// 2. <b>Profil dogrulamasi:</b> dosyadaki profil, calisan profille tam
///    karsilastirilir (model kimligi/revision/SHA-256/on isleme surumu/
///    embedding boyutu/ozellik turu/crop stratejisi/normalizasyon/sema
///    surumu). Herhangi biri farkliysa embedding'ler KULLANILMAZ ve
///    <see cref="IndexLoadOutcome.ProfileMismatch"/> donulur - kismi/karisik
///    index kabul edilmez, tam yeniden indeksleme gerekir (bkz.
///    docs/DECISIONS.md #95).
/// </summary>
public sealed class ProfiledIndexStore : IIndexStore
{
    private readonly EmbeddingProfile _profile;
    private readonly string _indexFolderName;

    /// <param name="profile">Bu store'un yazdigi/dogruladigi embedding profili.</param>
    /// <param name="indexFolderName">`.lens/indexes/` altindaki profil klasoru adi (orn. "dinov2-base-v1").</param>
    public ProfiledIndexStore(EmbeddingProfile profile, string indexFolderName)
    {
        if (string.IsNullOrWhiteSpace(indexFolderName))
        {
            throw new ArgumentException("Profil index klasor adi bos olamaz.", nameof(indexFolderName));
        }

        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _indexFolderName = indexFolderName;
    }

    /// <summary>[PILOT] DINOv2-Base icin store. Klasor adi <see cref="DinoV2BaseProfile.IndexFolderName"/> sabitinden gelir.</summary>
    public static ProfiledIndexStore ForDinoV2Base(EmbeddingProfile profile) =>
        new(profile, DinoV2BaseProfile.IndexFolderName);

    /// <summary>Bu store'un beklediği profil - yuklemede dosyadaki profil bununla karsilastirilir.</summary>
    public EmbeddingProfile Profile => _profile;

    /// <inheritdoc />
    public string Description => $"{_profile.ModelId} ({_indexFolderName})";

    /// <inheritdoc />
    public int ExpectedEmbeddingDimension => _profile.EmbeddingDimension;

    /// <inheritdoc />
    public string IndexDirectory(string productDirectory) =>
        AppPaths.ProfiledIndexDirectory(productDirectory, _indexFolderName);

    /// <inheritdoc />
    public string IndexFilePath(string productDirectory) =>
        AppPaths.ProfiledIndexFilePath(productDirectory, _indexFolderName);

    /// <inheritdoc />
    public string LockFilePath(string productDirectory) =>
        AppPaths.ProfiledIndexLockFilePath(productDirectory, _indexFolderName);

    /// <inheritdoc />
    public IndexLoadResult Load(string productDirectory, ILensLogger? logger = null)
    {
        var path = IndexFilePath(productDirectory);
        if (!File.Exists(path))
        {
            return new IndexLoadResult(IndexLoadOutcome.Missing, new List<ImageIndexEntry>(), null);
        }

        IndexDocument? document;
        try
        {
            var json = File.ReadAllText(path);
            document = JsonSerializer.Deserialize<IndexDocument>(json);
        }
        catch (Exception ex)
        {
            // Bozuk/yarim JSON veya bilinmeyen sema - dosyaya DOKUNULMAZ
            // (sonraki basarili Save atomic overwrite yapar), bu yuklemede
            // yok sayilir. Uygulama COKMEZ.
            logger?.Error("ProfiledIndexLoad", file: path, reason: ex.Message);
            return new IndexLoadResult(IndexLoadOutcome.Corrupt, new List<ImageIndexEntry>(), ex.Message);
        }

        if (document is null || document.Entries is null)
        {
            logger?.Warning("ProfiledIndexLoad", file: path, reason: "Index belgesi okunamadı (boş/bilinmeyen şema)");
            return new IndexLoadResult(IndexLoadOutcome.Corrupt, new List<ImageIndexEntry>(),
                "Index belgesi okunamadı (boş veya bilinmeyen şema)");
        }

        // Profil karsilastirmasi ONCE yapilir: uyumsuz bir dosyanin
        // kayitlarini dogrulamaya calismak anlamsizdir (orn. 512 boyutlu eski
        // vektorler "bozuk" degil, BASKA BIR MODELE ait olabilir - kullaniciya
        // dogru nedeni gostermek istiyoruz).
        var mismatch = _profile.DescribeMismatch(document.EmbeddingProfile);
        if (mismatch is not null || document.SchemaVersion != _profile.IndexSchemaVersion)
        {
            var reason = mismatch ?? $"index şema sürümü ({document.SchemaVersion} → {_profile.IndexSchemaVersion})";
            logger?.Warning("ProfiledIndexLoad", file: path,
                reason: $"Profil uyumsuz ({reason}) - index tamamen yeniden oluşturulacak");
            return new IndexLoadResult(IndexLoadOutcome.ProfileMismatch, new List<ImageIndexEntry>(), reason);
        }

        if (document.Entries.Any(e => !IndexEntryValidation.IsValid(e, ExpectedEmbeddingDimension)))
        {
            logger?.Warning("ProfiledIndexLoad", file: path,
                reason: "Kayıt içeriği geçersiz - yok sayıldı, yeniden oluşturulacak");
            return new IndexLoadResult(IndexLoadOutcome.Corrupt, new List<ImageIndexEntry>(),
                "Kayıt içeriği geçersiz");
        }

        return new IndexLoadResult(IndexLoadOutcome.Loaded, document.Entries, null);
    }

    /// <inheritdoc />
    public void Save(string productDirectory, List<ImageIndexEntry> entries)
    {
        Directory.CreateDirectory(IndexDirectory(productDirectory));
        var document = new IndexDocument
        {
            SchemaVersion = _profile.IndexSchemaVersion,
            EmbeddingProfile = _profile,
            Entries = entries,
        };

        var json = JsonSerializer.Serialize(document);
        AtomicFileWriter.WriteAllText(IndexFilePath(productDirectory), json);
    }
}
