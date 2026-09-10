using Lens.Core.Ai;
using Lens.Core.Logging;

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
public enum IndexWriteOutcome
{
    /// <summary>Kilit alindi, tarama/guncelleme yapildi ve diske kaydedildi.</summary>
    Updated,

    /// <summary>Baska bir writer kilidi tutuyor - hicbir scan/save baslamadi.</summary>
    LockUnavailable,

    /// <summary>Kilit alindi ama klasor taranamadi (orn. UNC erisilemedi) - eski index DEGISTIRILMEDI.</summary>
    ScanFailed,

    /// <summary>Tarama basarili oldu ama diske yazma basarisiz oldu - eski disk index BOZULMADI, in-memory eski index korunmali.</summary>
    SaveFailed,
}

/// <summary>Lock-guarded writer orkestrasyonunun sonucu. Entries alani, cagiranin ne zaman _indexEntries'i degistirmesi gerektigini Outcome'a gore yorumlamasi icindir (bkz. MainWindow.RunIndexUpdateAsync).</summary>
public sealed record IndexWriteResult(
    IndexWriteOutcome Outcome,
    List<ImageIndexEntry> Entries,
    IndexUpdateStats? Stats,
    Exception? Failure);

public static class ImageIndex
{
    /// <summary>Canonical shared index dosyasi: &lt;ProductDirectory&gt;/.lens/index.json. Side-effect-free (bkz. AppPaths.SharedIndexFilePath).</summary>
    public static string IndexPath(string folderPath) => LegacyClipIndexStore.Instance.IndexFilePath(folderPath);

    /// <summary>
    /// [Profil ayrimi] Bu sinif artik index'in NEREDE/HANGI bicimde
    /// saklandigini kendisi bilmez - bunu <see cref="IIndexStore"/> belirler.
    /// Store parametresi VERILMEYEN eski asiri yuklemeler, davranisi
    /// DEGISTIRMEMEK icin eski profilsiz CLIP dosyasini
    /// (<see cref="LegacyClipIndexStore"/>) kullanmaya devam eder.
    ///
    /// Boylece tarama/gecici-hata/atomic-yazma/kilit guvenilirlik mantigi
    /// (bkz. docs/DECISIONS.md #40, #55, #56, #62, #63) her model icin YENIDEN
    /// YAZILMAZ - tek kopya kalir.
    /// </summary>
    private static IIndexStore DefaultStore => LegacyClipIndexStore.Instance;

    /// <summary>
    /// [Reliability] Cache dosyasi bozuk/yarim JSON, deserialize edilemeyen
    /// icerik, veya gecersiz embedding (null/yanlis boyut/NaN/Infinity)
    /// icerebilir - onceki bir Lens surumunden kalma veya kesintiye ugramis
    /// bir yazimdan kaynaklanabilir. Bu durumda UYGULAMA COKMEZ: cache
    /// tamamen guvensiz sayilir (basit "hepsi ya da hicbiri" politikasi -
    /// kismi kurtarma/migration YOK), bos liste donulur, cagiran taraf
    /// (BuildOrUpdate/MainWindow) bunu normal "index yok, yeniden olustur"
    /// durumu gibi ele alir - kaynak klasor erisilebiliyorsa guvenli rebuild
    /// zaten dogal olarak gerceklesir.
    /// </summary>
    public static List<ImageIndexEntry> Load(string folderPath, ILensLogger? logger = null) =>
        DefaultStore.Load(folderPath, logger).Entries;

    /// <summary>
    /// [Shared index] Hedef klasor burada (yazma aninda) olusturulur - Load
    /// ve IndexPath side-effect-free kalir. Cagiran taraf normalde bunu
    /// dogrudan degil, BuildOrUpdateWithLock uzerinden (exclusive lock
    /// tutulurken) cagirmalidir.
    /// </summary>
    public static void Save(string folderPath, List<ImageIndexEntry> entries) =>
        DefaultStore.Save(folderPath, entries);

    /// <summary>
    /// Single-writer exclusive lock altinda calisan tam yazici orkestrasyonu:
    /// lock al -> (BuildOrUpdate zaten en basta Load ile index'i TAZE yeniden
    /// yukler, boylece lock oncesi stale state uzerinden yazilmaz) -> tara ->
    /// guncelle -> kaydet -> lock birak (using/finally ile her exception
    /// yolunda). Reader/arama bu metodu KULLANMAZ - yalnizca gercek
    /// scan/save/lock ihtiyaci olan yol (manuel "İndeksi Güncelle" ve
    /// auto-index acikken freshness sonrasi refresh) bunu cagirir.
    /// </summary>
    public static IndexWriteResult BuildOrUpdateWithLock(
        string folderPath, IImageEmbedder embedder, IProgress<(int Done, int Total)>? progress = null, ILensLogger? logger = null) =>
        BuildOrUpdateWithLock(folderPath, embedder, DefaultStore, progress, logger);

    /// <summary>[Profil ayrimi] Kilit ve kayit, verilen store'un KENDI klasorunde yapilir - baska bir modelin index'ine/kilidine DOKUNULMAZ.</summary>
    public static IndexWriteResult BuildOrUpdateWithLock(
        string folderPath, IImageEmbedder embedder, IIndexStore store,
        IProgress<(int Done, int Total)>? progress = null, ILensLogger? logger = null)
    {
        using var handle = IndexLock.TryAcquire(folderPath, store, out var lockFailure);
        if (handle is null)
        {
            // Kilit alinamadi: hicbir scan/save baslamadi. Cagiran taraf,
            // bellekte zaten yuklu bir stable index varsa onunla aramaya
            // devam edebilir (bu metod o karari vermez, sadece bildirir).
            //
            // [Profil ayrimi] Burada donen liste de store uzerinden okunur -
            // profil uyumsuzsa BOS doner, yani uyumsuz embedding'ler kilit
            // alinamadigi durumda bile aramaya SIZMAZ.
            return new IndexWriteResult(
                IndexWriteOutcome.LockUnavailable, store.Load(folderPath, logger).Entries, null, lockFailure);
        }

        var (entries, stats) = BuildOrUpdate(folderPath, embedder, store, progress, logger);
        if (stats.ScanError is not null)
        {
            return new IndexWriteResult(IndexWriteOutcome.ScanFailed, entries, stats, null);
        }

        try
        {
            store.Save(folderPath, entries);
        }
        catch (Exception ex)
        {
            // [Network safety] Save basarisiz oldu - eski diskteki index.json
            // AtomicFileWriter sayesinde BOZULMADI (temp yazilamadan/replace
            // edilemeden hata olustu). entries burada yeni (henuz kaydedilmemis)
            // hesaplanan liste - cagiran taraf bunu ONEMLI: in-memory state'e
            // YANSITMAMALI (sahte "guncel" gorunumu olusmasin), sadece hatayi
            // gostermeli.
            return new IndexWriteResult(IndexWriteOutcome.SaveFailed, entries, stats, ex);
        }

        return new IndexWriteResult(IndexWriteOutcome.Updated, entries, stats, null);
    }

    /// <summary>
    /// Klasoru tarar; degismeyen dosyalar icin var olan embedding'i yeniden
    /// kullanir, yeni/degismis dosyalar icin embedder ile yeniden hesaplar,
    /// artik var olmayan dosyalari cikarir. Okunamayan/bozuk bir gorsel tum
    /// islemi durdurmaz; SupportedImageButFailed olarak Issues'a eklenir.
    /// Taramanin KENDISI basarisiz olursa (orn. UNC yola hic ulasilamadi),
    /// eski index DEGISTIRILMEDEN doner - hicbir sey kaybedilmez/bozulmaz.
    ///
    /// [Reliability] Tarama dosyayi hala goruyor ama TEK dosyanin islenmesi
    /// (metadata okuma veya embed) gecici bir nedenle (network kesintisi,
    /// dosya kilidi, izin sorunu) basarisiz olursa ve bu dosyanin daha once
    /// SAGLAM bir kaydi varsa, o eski kayit sonuca AYNEN tasinir - "removed"
    /// sayilmaz ve cache'den kaybolmaz. Yalnizca directory snapshot'inda hic
    /// gorunmeyen dosyalar (gercekten silinmis) removed sayilir.
    /// </summary>
    public static (List<ImageIndexEntry> Entries, IndexUpdateStats Stats) BuildOrUpdate(
        string folderPath, IImageEmbedder embedder, IProgress<(int Done, int Total)>? progress = null, ILensLogger? logger = null) =>
        BuildOrUpdate(folderPath, embedder, DefaultStore, progress, logger);

    /// <summary>
    /// [Profil ayrimi] Mevcut kayitlar verilen store'dan okunur. Store,
    /// dosyayi profil uyumsuz (baska model/on isleme) veya bozuk bulursa BOS
    /// liste doner - o zaman hicbir dosya "unchanged" sayilamaz ve TUM klasor
    /// yeniden embed edilir (kismi/karisik index YOK, bkz.
    /// docs/DECISIONS.md #95). Nedeni <see cref="IndexUpdateStats.IndexResetReason"/>
    /// alaninda kullaniciya gosterilmek uzere tasinir.
    /// </summary>
    public static (List<ImageIndexEntry> Entries, IndexUpdateStats Stats) BuildOrUpdate(
        string folderPath, IImageEmbedder embedder, IIndexStore store,
        IProgress<(int Done, int Total)>? progress = null, ILensLogger? logger = null)
    {
        var stats = new IndexUpdateStats();
        var load = store.Load(folderPath, logger);
        var existingEntries = load.Entries;
        if (load.Outcome is IndexLoadOutcome.ProfileMismatch or IndexLoadOutcome.Corrupt)
        {
            stats.IndexResetReason = load.Reason;
        }

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
            // [Reliability] Lookup TRY bloğunun disinda - FileInfo erisimi
            // basarisiz olsa bile existingEntry catch icinde kullanilabilsin.
            existingByPath.TryGetValue(relativePath, out var existingEntry);

            try
            {
                var fileInfo = new FileInfo(filePath);
                var sizeBytes = fileInfo.Length;
                var lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;

                var isUnchanged = existingEntry is not null
                    && existingEntry.FileSizeBytes == sizeBytes
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
                // [Reliability] Dosya metadata okuma VEYA embed etme sirasinda
                // GECICI bir nedenle basarisiz oldu (network kesintisi, dosya
                // kilidi, izin sorunu, bozuk dosya). Dosya klasorde hala
                // GORULUYOR (supportedFiles listesinde) - bu yuzden "silinmis"
                // degil, "su an islenemedi" durumu. Eski saglam kayit varsa
                // AYNEN korunur ki tek seferlik/gecici bir okuma hatasi
                // urunun embedding'ini kalici olarak kaybettirmesin.
                if (existingEntry is not null)
                {
                    result.Add(existingEntry);
                }

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
    public static ChangeSummary DetectChanges(string folderPath, ILensLogger? logger = null) =>
        DetectChanges(folderPath, DefaultStore, logger);

    /// <summary>
    /// [Profil ayrimi] Store profil uyumsuzlugu/bozulma bildirirse kayitli
    /// hicbir dosya "unchanged" sayilamaz: mevcut butun dosyalar "new" olarak
    /// raporlanir (dolayisiyla <see cref="ChangeSummary.HasChanges"/> true
    /// olur ve arama oncesi otomatik indeksleme tam yeniden olusturmayi
    /// tetikler), neden ise <see cref="ChangeSummary.IndexResetReason"/>
    /// alaninda tasinir.
    /// </summary>
    public static ChangeSummary DetectChanges(string folderPath, IIndexStore store, ILensLogger? logger = null)
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

        var load = store.Load(folderPath, logger);
        if (load.Outcome is IndexLoadOutcome.ProfileMismatch or IndexLoadOutcome.Corrupt)
        {
            return new ChangeSummary(
                NewCount: supportedFiles.Count, ChangedCount: 0, RemovedCount: 0, UnchangedCount: 0,
                ScanError: null, IndexResetReason: load.Reason);
        }

        var existingByPath = load.Entries.ToDictionary(e => e.RelativePath, e => e);
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
            var fileName = Path.GetFileName(filePath);
            if (FileClassifier.IsKnownHarmless(fileName))
            {
                // Lens'in kendi eski artefakti veya Windows'un otomatik
                // dosyasi - kullanicinin sucu degil, "sorun" olarak hic
                // sayilmaz/gorunmez (Issues'a girmez, sayaclara girmez).
                continue;
            }

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
                    // [Kullanici geri bildirimi] Urun klasoru esas olarak
                    // gorsel icindir - bir .pdf/.zip/.txt vb. gorulunce
                    // sessizce yok sayilmaz, Issues'a da eklenir ki
                    // kullaniciya UI/log uzerinden gorunur olsun. Yine de
                    // decode DENENMEZ ve indeksleme durmaz - yalnizca
                    // gorunurluk ekleniyor.
                    skippedCount++;
                    unsupportedIssues.Add(new IndexFileIssue(
                        Path.GetFileName(filePath), extension, FileIssueKind.NonImageFile,
                        "Desteklenmeyen dosya türü"));
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
