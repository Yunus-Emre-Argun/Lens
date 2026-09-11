using Lens.Core.Logging;

namespace Lens.Core.DesenCodes;

/// <summary>Toplu guncellemenin nasil bittigi.</summary>
public enum DesenCodeRefreshOutcome
{
    /// <summary>Tum dosyalar islendi ve sonuc kaydedildi.</summary>
    Completed,

    /// <summary>Kullanici iptal etti. O ana kadar alinan GECERLI kodlar kaydedilir.</summary>
    Cancelled,

    /// <summary>Art arda cok fazla baglanti hatasi olustu - islem erken durduruldu (binlerce zaman asimi beklenmez).</summary>
    AbortedAfterFailures,

    /// <summary>Baska bir kullanici/islem kod guncellemesi yapiyor.</summary>
    LockUnavailable,

    /// <summary>Metadata kataloga yazilamadi (izin/ag). Kodlar KAYDEDILMEDI.</summary>
    SaveFailed,
}

/// <param name="Outcome">Sonuc turu.</param>
/// <param name="TotalFiles">Katalogdaki islenecek dosya sayisi.</param>
/// <param name="UniqueQueries">Tekillestirmeden sonra servise gonderilen sorgu sayisi.</param>
/// <param name="Updated">Yeni/degismis kod yazilan kayit sayisi.</param>
/// <param name="NotFound">Servisin "kod yok" dedigi kayit sayisi.</param>
/// <param name="Failed">Servise ulasilamayan veya gecersiz cevap alinan kayit sayisi (eski kod KORUNDU).</param>
/// <param name="Failure">Kaydetme/kilit hatasi varsa.</param>
/// <param name="FailureDetail">
/// Son servis hatasinin aciklamasi (SOAP faultstring, zaman asimi vb.).
/// Erken durusta kullaniciya TEK bir anlasilir mesaj gosterilebilsin diye
/// tutulur - her dosya icin ayri uyari acilmaz.
/// </param>
public sealed record DesenCodeRefreshResult(
    DesenCodeRefreshOutcome Outcome,
    int TotalFiles,
    int UniqueQueries,
    int Updated,
    int NotFound,
    int Failed,
    Exception? Failure,
    string? FailureDetail = null);

/// <summary>
/// Katalogdaki dosyalar icin desen kodlarini servisten toplu olarak alir ve
/// ortak metadata dosyasina yazar.
///
/// Bu, VPN erisimi olan bilgisayarda ELLE calistirilan bir bakim islemidir -
/// normal aramada ASLA otomatik tetiklenmez.
///
/// Davranis kurallari (bkz. gorev tanimi §5-§6):
///   • Servis hatasi MEVCUT GECERLI KODLARI SILMEZ; eski kod ve eski
///     guncelleme tarihi aynen korunur.
///   • Bos cevap gercek kod gibi KAYDEDILMEZ ("kod yok" olarak kaydedilir).
///   • Baglanti hatasi "kod bulunamadi" diye KALICILASTIRILMAZ.
///   • Ayni dosya adi bir kez sorgulanir (tekillestirme).
///   • Art arda cok fazla hatada islem DURUR. Bu, METODUN BULUNAMAMASI gibi
///     TUM urunleri ayni sekilde etkileyen bir hatayi da kapsar: boyle bir
///     durumda binlerce dosya icin ayni cagri tekrarlanmaz, tek bir durum
///     mesaji uretilir. Esik ozellikle metin eslestirmesine
///     (orn. faultstring == "Method not found") DAYANDIRILMADI - servisin
///     hata metni bilinmiyor, tahmine dayali bir kural yanlis durumda
///     sessizce calismazdi.
///   • Artik katalogda olmayan dosyalarin kayitlari temizlenir - kod baska
///     bir dosyaya TASINMAZ.
/// </summary>
public static class DesenCodeRefresh
{
    /// <summary>
    /// Verilen dosyalar icin kodlari gunceller.
    /// </summary>
    /// <param name="productDirectory">Katalog koku - metadata buranin altina yazilir.</param>
    /// <param name="relativePaths">Katalog kokune gore goreli dosya yollari (mevcut tarama sonucundan gelir - kapsam BURADA degistirilmez).</param>
    /// <param name="service">Sorgulama servisi.</param>
    /// <param name="store">Metadata deposu.</param>
    /// <param name="progress">(tamamlanan, toplam) ilerleme bildirimi.</param>
    /// <param name="abortAfterConsecutiveFailures">Art arda bu kadar hatadan sonra islem durur; yapilandirmadan gelir (bkz. <see cref="DesenCodeServiceOptions.AbortAfterConsecutiveFailures"/>).</param>
    /// <param name="cancellationToken">Iptal.</param>
    /// <param name="logger">Istege bagli log.</param>
    public static async Task<DesenCodeRefreshResult> RunAsync(
        string productDirectory,
        IReadOnlyList<string> relativePaths,
        IDesenCodeService service,
        DesenCodeStore store,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default,
        ILensLogger? logger = null,
        int abortAfterConsecutiveFailures = DesenCodeServiceOptionsDefaults.AbortThreshold)
    {
        using var handle = store.TryAcquireLock(productDirectory, out var lockFailure);
        if (handle is null)
        {
            return new DesenCodeRefreshResult(
                DesenCodeRefreshOutcome.LockUnavailable, relativePaths.Count, 0, 0, 0, 0, lockFailure);
        }

        // Mevcut kayitlar TAZE okunur - kilit oncesi eskimis bir goruntu
        // uzerine yazmamak icin.
        var existing = store.Load(productDirectory, logger).Metadata;
        var byPath = existing.Entries
            .GroupBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // [Tekillestirme] Servis yalnizca dosya ADINI kabul ettigi icin, ayni
        // ada sahip farkli yollar TEK sorgu ile karsilanir. Bu bir belirsizlik
        // kaynagidir (ayni adli farkli icerikli dosyalar ayni kodu alir) -
        // dokumantasyonda acikca belirtilmistir.
        var groups = relativePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int updated = 0, notFound = 0, failed = 0, consecutiveFailures = 0, done = 0;
        string? lastFailureDetail = null;
        var results = new Dictionary<string, DesenCodeEntry>(StringComparer.OrdinalIgnoreCase);
        var outcome = DesenCodeRefreshOutcome.Completed;
        var now = DateTimeOffset.UtcNow;

        foreach (var group in groups)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                outcome = DesenCodeRefreshOutcome.Cancelled;
                break;
            }

            DesenCodeLookupResult lookup;
            try
            {
                lookup = await service.LookupAsync(group.Key, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                outcome = DesenCodeRefreshOutcome.Cancelled;
                break;
            }
            catch (Exception ex)
            {
                lookup = DesenCodeLookupResult.Unavailable(ex.Message);
            }

            foreach (var relativePath in group)
            {
                byPath.TryGetValue(relativePath, out var previous);
                var entry = Apply(relativePath, previous, lookup, now);
                if (entry is not null)
                {
                    results[relativePath] = entry;
                }
            }

            switch (lookup.Status)
            {
                case DesenCodeLookupStatus.Found:
                    updated += group.Count();
                    consecutiveFailures = 0;
                    break;

                case DesenCodeLookupStatus.NotFound:
                    notFound += group.Count();
                    consecutiveFailures = 0;
                    break;

                default:
                    failed += group.Count();
                    consecutiveFailures++;
                    lastFailureDetail = lookup.Detail;

                    // Log'a her hata yazilir, ama KULLANICIYA her dosya icin
                    // ayri bir uyari GOSTERILMEZ - tek durum mesaji cagiran
                    // katmanda uretilir.
                    logger?.Warning("DesenCodeLookup", file: group.Key, reason: lookup.Detail ?? "bilinmeyen hata");
                    break;
            }

            done++;
            progress?.Report((done, groups.Count));

            if (consecutiveFailures >= Math.Max(1, abortAfterConsecutiveFailures))
            {
                outcome = DesenCodeRefreshOutcome.AbortedAfterFailures;
                break;
            }
        }

        // Islenmemis (iptal/erken durus) dosyalarin ESKI kayitlari aynen
        // korunur - yarim kalan bir islem mevcut kodlari SILMEZ.
        foreach (var relativePath in relativePaths)
        {
            if (!results.ContainsKey(relativePath) && byPath.TryGetValue(relativePath, out var previous))
            {
                results[relativePath] = previous;
            }
        }

        // Artik katalogda olmayan dosyalarin kayitlari DUSER - kod baska bir
        // dosyaya tasinmaz (byPath'te olup relativePaths'te olmayanlar).
        existing.Entries = results.Values.ToList();
        existing.SourceService = DescribeSource(service);
        if (outcome == DesenCodeRefreshOutcome.Completed)
        {
            existing.LastSuccessfulRefreshUtc = now;
        }

        try
        {
            store.Save(productDirectory, existing);
        }
        catch (Exception ex)
        {
            logger?.Error("DesenCodeSave", file: DesenCodeStore.MetadataFilePath(productDirectory), reason: ex.Message);
            return new DesenCodeRefreshResult(
                DesenCodeRefreshOutcome.SaveFailed, relativePaths.Count, groups.Count, updated, notFound, failed, ex,
                lastFailureDetail);
        }

        return new DesenCodeRefreshResult(
            outcome, relativePaths.Count, groups.Count, updated, notFound, failed, null, lastFailureDetail);
    }

    /// <summary>
    /// Tek bir kayit icin yeni durumu belirler. KRITIK: servis hatasinda
    /// (<see cref="DesenCodeLookupStatus.ServiceUnavailable"/> /
    /// <see cref="DesenCodeLookupStatus.InvalidResponse"/>) eski kayit
    /// AYNEN korunur - kodu da, guncelleme tarihi de degismez.
    /// </summary>
    internal static DesenCodeEntry? Apply(
        string relativePath, DesenCodeEntry? previous, DesenCodeLookupResult lookup, DateTimeOffset now) =>
        lookup.Status switch
        {
            DesenCodeLookupStatus.Found => new DesenCodeEntry
            {
                RelativePath = relativePath,
                QueriedFileName = lookup.QueriedFileName ?? Path.GetFileName(relativePath),
                Code = lookup.Code,
                State = DesenCodeEntryState.Found,
                UpdatedUtc = now,
            },

            DesenCodeLookupStatus.NotFound => new DesenCodeEntry
            {
                RelativePath = relativePath,
                QueriedFileName = lookup.QueriedFileName ?? Path.GetFileName(relativePath),
                Code = null,
                State = DesenCodeEntryState.NotFound,
                UpdatedUtc = now,
            },

            // Servis hatasi / gecersiz cevap: eski kayit varsa DOKUNULMAZ,
            // yoksa hicbir kayit OLUSTURULMAZ (bos bir "bulunamadi" yazmak,
            // gecici ag hatasini kalici hale getirirdi).
            _ => previous,
        };

    private static string DescribeSource(IDesenCodeService service) => service.GetType().Name;
}

/// <summary>Orkestrasyonun servis nesnesinden bagimsiz calisabilmesi icin varsayilan esik degeri (test edilebilirlik).</summary>
public static class DesenCodeServiceOptionsDefaults
{
    /// <summary>Yapilandirma verilmediginde kullanilan art arda hata esigi.</summary>
    public const int AbortThreshold = 5;
}
