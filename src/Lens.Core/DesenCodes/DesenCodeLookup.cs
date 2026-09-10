namespace Lens.Core.DesenCodes;

/// <summary>
/// Tek bir sorgunun sonucu. "Kod yok", "servise erisilemedi" ve "gecersiz
/// cevap" BILEREK ayri durumlardir - ucunu tek bir "basarisiz"a indirgemek,
/// gecici bir ag hatasinin kalici "kod bulunamadi" olarak kaydedilmesine yol
/// acardi (bkz. gorev tanimi §5).
/// </summary>
public enum DesenCodeLookupStatus
{
    /// <summary>Servis gecerli, bos olmayan bir kod dondurdu.</summary>
    Found,

    /// <summary>Servis calisti ve bu dosya adi icin kod OLMADIGINI bildirdi (bos/null cevap).</summary>
    NotFound,

    /// <summary>Servise ulasilamadi, zaman asimi olustu veya SOAP fault dondu. Mevcut kayit KORUNMALIDIR.</summary>
    ServiceUnavailable,

    /// <summary>Cevap alindi ama ayristirilamadi/beklenen alan yoktu. Mevcut kayit KORUNMALIDIR.</summary>
    InvalidResponse,
}

/// <summary>Bir dosya adi sorgusunun sonucu.</summary>
/// <param name="Status">Sonuc turu.</param>
/// <param name="Code">Yalnizca <see cref="DesenCodeLookupStatus.Found"/> durumunda dolu; bastaki sifirlar KORUNUR (string olarak saklanir).</param>
/// <param name="QueriedFileName">Servise gercekte gonderilen ad (uzantili veya uzantisiz) - hangi bicimin eslestigi metadata'ya yazilir.</param>
/// <param name="Detail">Hata/tani ayrintisi; kullaniciya gosterilebilir, kimlik bilgisi ICERMEZ.</param>
public sealed record DesenCodeLookupResult(
    DesenCodeLookupStatus Status,
    string? Code,
    string? QueriedFileName,
    string? Detail)
{
    /// <summary>Servis gecerli bir kod dondurdu.</summary>
    public static DesenCodeLookupResult Found(string code, string queriedFileName) =>
        new(DesenCodeLookupStatus.Found, code, queriedFileName, null);

    /// <summary>Servis calisti, bu dosya icin kod YOK. Sonraki acik yenilemede tekrar denenir.</summary>
    public static DesenCodeLookupResult NotFound(string queriedFileName) =>
        new(DesenCodeLookupStatus.NotFound, null, queriedFileName, null);

    /// <summary>Servise ulasilamadi / zaman asimi / SOAP fault. Mevcut kayit KORUNUR.</summary>
    public static DesenCodeLookupResult Unavailable(string detail) =>
        new(DesenCodeLookupStatus.ServiceUnavailable, null, null, detail);

    /// <summary>Cevap ayristirilamadi veya beklenen alan yoktu. Mevcut kayit KORUNUR.</summary>
    public static DesenCodeLookupResult Invalid(string detail) =>
        new(DesenCodeLookupStatus.InvalidResponse, null, null, detail);
}

/// <summary>
/// Desen kodu sorgulama sozlesmesi. Soyutlanmasinin nedeni, uretim
/// istemcisinin CANLI OLARAK DOGRULANAMAMIS olmasidir (hedef metot serviste
/// bulunamadi): testler sahte bir uygulamayla kosar, uretim istemcisi
/// degistirildiginde cagiran katman etkilenmez.
/// </summary>
public interface IDesenCodeService
{
    /// <summary>
    /// Verilen dosya adi icin desen kodunu sorgular. YALNIZCA dosya adi
    /// gonderilir - tam yerel yol, UNC yolu veya dosya icerigi ASLA
    /// gonderilmez.
    /// </summary>
    Task<DesenCodeLookupResult> LookupAsync(string fileName, CancellationToken cancellationToken);
}
