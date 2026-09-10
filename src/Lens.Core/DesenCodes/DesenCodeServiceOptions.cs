namespace Lens.Core.DesenCodes;

/// <summary>
/// Desen kodu servisinin cagri sozlesmesi. BILEREK yapilandirilabilir:
/// gorev sirasinda verilen metot adi (<c>GetDesenKodu</c>) hedef uctaki
/// WSDL'de BULUNAMADI (bkz. docs/DESEN_CODE_SERVICE.md "Doğrulama"), bu
/// yuzden metot/parametre adlari koda GOMULMEZ - dogru ad ogrenildiginde
/// yeniden derleme degil, tek satirlik bir ayar degisikligi yeterlidir.
///
/// Degerler exe yanindaki <c>appsettings.json</c> dosyasindan okunur
/// (IT/sistem yoneticisi duzenler - bkz. docs/DECISIONS.md #41). Repo'daki
/// ornek dosya BOS gelir; gercek adres hardcode EDILMEZ.
///
/// Kimlik bilgisi alani BILEREK YOKTUR: uc, kimliksiz cagriya HTTP 200
/// donmektedir ve VPN girisi ile servis kimlik dogrulamasi AYNI SEY DEGILDIR.
/// </summary>
public sealed class DesenCodeServiceOptions
{
    /// <summary>Tam .asmx adresi. Bos ise servis yapilandirilmamis sayilir ve kod guncelleme islemi calismaz.</summary>
    public string? Endpoint { get; set; }

    /// <summary>SOAP metot adi. Bos ise servis yapilandirilmamis sayilir.</summary>
    public string? MethodName { get; set; }

    /// <summary>Metodun tek string parametresinin adi.</summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// XML namespace'i. Hedef ASMX servisinin WSDL'inde dogrulanan deger
    /// <c>http://tempuri.org/</c>'dur; SOAPAction bundan + metot adindan
    /// turetilir (ayni WSDL'de gercek metotlarla dogrulandi).
    /// </summary>
    public string Namespace { get; set; } = "http://tempuri.org/";

    /// <summary>Tek bir cagri icin zaman asimi (saniye).</summary>
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Art arda bu kadar baglanti/zaman asimi hatasi olusursa toplu guncelleme
    /// DURUR. Binlerce dosyada her biri icin zaman asimi beklemek kabul
    /// edilemez (bkz. gorev tanimi §6).
    /// </summary>
    public int AbortAfterConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// Sorguda dosya adi uzantisiyla mi gonderilsin? Varsayilan true
    /// (<c>desen_adi.jpg</c>). Gercek ornekler uzantisiz ad gerektigini
    /// gosterirse false yapilabilir - bu, DOGRULANMIS bicimin uygulanmasidir,
    /// tahmin degil.
    /// </summary>
    public bool SendFileExtension { get; set; } = true;

    /// <summary>
    /// Uzantili sorgu acikca "bulunamadi" donerse uzantisiz ad ikinci kez
    /// denensin mi. Bu fallback yalnizca ACIK "bulunamadi" cevabinda calisir;
    /// baglanti hatasi, zaman asimi veya SOAP fault durumunda ASLA denenmez.
    /// </summary>
    public bool TryWithoutExtensionOnNotFound { get; set; } = true;

    /// <summary>Zorunlu alanlar dolu mu - degilse "Desen Kodlarını Güncelle" islemi baslatilmaz.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(MethodName)
        && !string.IsNullOrWhiteSpace(ParameterName);

    /// <summary>Yapilandirma eksikse kullaniciya gosterilecek, ne yapilacagini soyleyen mesaj.</summary>
    public string DescribeMissingConfiguration() =>
        "Desen kodu servisi yapılandırılmamış. Uygulama klasöründeki appsettings.json "
        + "dosyasında DesenCodeService bölümünün Endpoint, MethodName ve ParameterName "
        + "alanları doldurulmalıdır.";
}
