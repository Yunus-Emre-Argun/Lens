namespace Lens.Core.Indexing;

/// <summary>
/// Tek-yazarli (single-writer) exclusive kilit: &lt;ProductDirectory&gt;/.lens/index.lock
/// uzerinde FileShare.None ile alinan bir OS dosya handle'i. Handle acik
/// oldugu surece baska hicbir process/instance ayni dosyayi acamaz - fiziksel
/// dosyanin VARLIGI degil, bu ACIK HANDLE aktif kilidi temsil eder (process
/// beklenmedik sekilde sonlanirsa OS handle'i otomatik serbest birakir).
///
/// Kapsam kasitli olarak minimal tutuldu: distributed lock/queue/servis YOK,
/// yalnizca tek bir paylasilan (UNC olabilir) klasor icin basit dosya kilidi.
/// </summary>
public sealed class IndexLock : IDisposable
{
    private readonly FileStream _stream;

    private IndexLock(FileStream stream) => _stream = stream;

    /// <summary>
    /// Kilidi almayi dener. Basarili olursa Dispose edilene kadar tutulmasi
    /// gereken bir handle doner. Basarisiz olursa null doner:
    /// - <paramref name="failure"/> null ise: kilit BASKA BIR YAZAR tarafindan
    ///   tutuluyor (kullaniciya standart "başka bir kullanıcı tarafından
    ///   güncelleniyor" mesaji gosterilmeli).
    /// - <paramref name="failure"/> doluysa: ".lens" klasorune/kilit dosyasina
    ///   erisimde farkli bir sorun var (izin, ag erisilemezligi vb.) - cagiran
    ///   taraf bunu ayri, daha spesifik bir hata olarak ele alabilir.
    /// </summary>
    public static IndexLock? TryAcquire(string productDirectory, out Exception? failure) =>
        TryAcquire(productDirectory, LegacyClipIndexStore.Instance, out failure);

    /// <summary>
    /// [Profil ayrimi] Kilidi, verilen store'un KENDI klasorunde alir - eski
    /// CLIP kilidi (`.lens/index.lock`) ile profil-ozel kilit
    /// (`.lens/indexes/&lt;profil&gt;/index.lock`) AYRI dosyalardir, dolayisiyla
    /// farkli modellerin yazicilari birbirini BLOKLAMAZ ve birbirinin
    /// dosyasina dokunmaz. Sozlesme (FileShare.None, handle = aktif kilit,
    /// failure semantigi) DEGISMEDI.
    /// </summary>
    public static IndexLock? TryAcquire(string productDirectory, IIndexStore store, out Exception? failure)
    {
        failure = null;
        string lockPath;
        try
        {
            var dir = store.IndexDirectory(productDirectory);
            Directory.CreateDirectory(dir);
            lockPath = store.LockFilePath(productDirectory);
        }
        catch (Exception ex)
        {
            failure = ex;
            return null;
        }

        try
        {
            var stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new IndexLock(stream);
        }
        catch (IOException)
        {
            // Sharing violation - baska bir writer handle'i acik tutuyor.
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            failure = ex;
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
