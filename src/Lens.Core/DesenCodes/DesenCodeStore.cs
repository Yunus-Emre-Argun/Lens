using System.Text.Json;
using Lens.Core.Config;
using Lens.Core.IO;
using Lens.Core.Logging;

namespace Lens.Core.DesenCodes;

/// <summary>Metadata yuklemesinin sonucu - "dosya yok" ile "dosya bozuk" ayri raporlanir.</summary>
public enum DesenCodeLoadOutcome
{
    /// <summary>Dosya hic yok - kodlar henuz hazirlanmamis (normal ilk durum).</summary>
    Missing,

    /// <summary>Dosya okundu ve gecerli.</summary>
    Loaded,

    /// <summary>Dosya var ama bozuk/yarim JSON veya bilinmeyen sema - KULLANILMAZ.</summary>
    Invalid,
}

/// <param name="Outcome">Yukleme sonucu.</param>
/// <param name="Metadata">Yalnizca <see cref="DesenCodeLoadOutcome.Loaded"/> durumunda dolu; aksi halde BOS bir metadata.</param>
/// <param name="Reason">Gecersizlik nedeni (kullaniciya gosterilebilir).</param>
public sealed record DesenCodeLoadResult(DesenCodeLoadOutcome Outcome, DesenCodeMetadata Metadata, string? Reason);

/// <summary>
/// Desen kodu metadata dosyasinin okunmasi/yazilmasi.
///
/// Konum: <c>&lt;UrunDizini&gt;/.lens/metadata/desen-codes-v1.json</c>
/// Kilit : <c>&lt;UrunDizini&gt;/.lens/metadata/desen-codes.lock</c>
///
/// Embedding index'lerinden ve onlarin kilitlerinden TAMAMEN AYRIDIR - kod
/// guncelleme ile indeksleme birbirini bloklamaz ve birbirinin dosyasina
/// dokunmaz.
///
/// Yazim atomiktir (<see cref="AtomicFileWriter"/>): okuyan kullanicilar
/// yarim yazilmis JSON gormez.
/// </summary>
public sealed class DesenCodeStore
{
    private const string MetadataFolderName = "metadata";
    private const string MetadataFileName = "desen-codes-v1.json";
    private const string LockFileName = "desen-codes.lock";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>Side-effect-free: bu yolu OGRENMEK klasor OLUSTURMAZ.</summary>
    public static string MetadataDirectory(string productDirectory) =>
        Path.Combine(AppPaths.SharedIndexDirectory(productDirectory), MetadataFolderName);

    /// <summary>Metadata dosyasinin tam yolu. Side-effect-free: OGRENMEK dosyayi/klasoru OLUSTURMAZ.</summary>
    public static string MetadataFilePath(string productDirectory) =>
        Path.Combine(MetadataDirectory(productDirectory), MetadataFileName);

    /// <summary>Kod guncellemesine ozel kilit dosyasi - embedding index kilidinden AYRIDIR.</summary>
    public static string LockFilePath(string productDirectory) =>
        Path.Combine(MetadataDirectory(productDirectory), LockFileName);

    /// <summary>
    /// Metadata'yi okur. UYGULAMA COKMEZ: bozuk/yarim dosya veya bilinmeyen
    /// sema exception yerine <see cref="DesenCodeLoadOutcome.Invalid"/> ile
    /// bildirilir ve BOS metadata donulur - hicbir kod gosterilmez, ama
    /// arama calismaya devam eder.
    /// </summary>
    public DesenCodeLoadResult Load(string productDirectory, ILensLogger? logger = null)
    {
        var path = MetadataFilePath(productDirectory);
        if (!File.Exists(path))
        {
            return new DesenCodeLoadResult(DesenCodeLoadOutcome.Missing, new DesenCodeMetadata(), null);
        }

        DesenCodeMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<DesenCodeMetadata>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            logger?.Error("DesenCodeLoad", file: path, reason: ex.Message);
            return new DesenCodeLoadResult(DesenCodeLoadOutcome.Invalid, new DesenCodeMetadata(), ex.Message);
        }

        if (metadata is null || metadata.Entries is null)
        {
            logger?.Warning("DesenCodeLoad", file: path, reason: "Belge okunamadı (boş/bilinmeyen şema)");
            return new DesenCodeLoadResult(DesenCodeLoadOutcome.Invalid, new DesenCodeMetadata(),
                "Belge okunamadı (boş veya bilinmeyen şema)");
        }

        if (metadata.SchemaVersion != DesenCodeMetadata.CurrentSchemaVersion)
        {
            logger?.Warning("DesenCodeLoad", file: path,
                reason: $"Bilinmeyen şema sürümü {metadata.SchemaVersion} (beklenen {DesenCodeMetadata.CurrentSchemaVersion})");
            return new DesenCodeLoadResult(DesenCodeLoadOutcome.Invalid, new DesenCodeMetadata(),
                $"Bilinmeyen şema sürümü: {metadata.SchemaVersion}");
        }

        // Anahtarsiz kayitlar sessizce atilir - kodu olmayan bir "Found"
        // kaydi da tutarsizdir ve gosterilmemelidir.
        metadata.Entries = metadata.Entries
            .Where(e => e is not null
                && !string.IsNullOrWhiteSpace(e.RelativePath)
                && (e.State != DesenCodeEntryState.Found || !string.IsNullOrWhiteSpace(e.Code)))
            .ToList();

        return new DesenCodeLoadResult(DesenCodeLoadOutcome.Loaded, metadata, null);
    }

    /// <summary>
    /// Metadata'yi atomik olarak yazar. Yazma izni yoksa/erisilemezse
    /// <see cref="UnauthorizedAccessException"/> veya
    /// <see cref="IOException"/> YUKARI TASINIR - cagiran taraf bunu
    /// kullaniciya bildirmeli, "kaydedildi" gibi davranMAMALIdir. Sessizce
    /// kullanici ayarlari dizinine YAZILMAZ.
    /// </summary>
    public void Save(string productDirectory, DesenCodeMetadata metadata)
    {
        Directory.CreateDirectory(MetadataDirectory(productDirectory));
        metadata.SchemaVersion = DesenCodeMetadata.CurrentSchemaVersion;

        // Kararli sira: dosya diff'lenebilir kalsin ve her kayitta gereksiz
        // tam-dosya degisikligi gorunmesin.
        metadata.Entries = metadata.Entries
            .OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        AtomicFileWriter.WriteAllText(
            MetadataFilePath(productDirectory),
            JsonSerializer.Serialize(metadata, SerializerOptions));
    }

    /// <summary>
    /// Kod guncellemesi icin tek-yazarli exclusive kilit. Embedding index
    /// kilidinden AYRI bir dosyadir - kod guncelleme ile indeksleme birbirini
    /// bloklamaz.
    /// </summary>
    public DesenCodeLock? TryAcquireLock(string productDirectory, out Exception? failure)
    {
        failure = null;
        string lockPath;
        try
        {
            Directory.CreateDirectory(MetadataDirectory(productDirectory));
            lockPath = LockFilePath(productDirectory);
        }
        catch (Exception ex)
        {
            failure = ex;
            return null;
        }

        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new DesenCodeLock(stream);
        }
        catch (IOException)
        {
            // Baska bir yazar kilidi tutuyor.
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            failure = ex;
            return null;
        }
    }
}

/// <summary>Desen kodu metadata'si icin tek-yazarli kilit (bkz. <see cref="Lens.Core.Indexing.IndexLock"/> ile ayni desen, AYRI dosya).</summary>
public sealed class DesenCodeLock : IDisposable
{
    private readonly FileStream _stream;

    internal DesenCodeLock(FileStream stream) => _stream = stream;

    /// <summary>Kilidi birakir - handle kapandigi anda baska bir yazar kilidi alabilir.</summary>
    public void Dispose() => _stream.Dispose();
}
