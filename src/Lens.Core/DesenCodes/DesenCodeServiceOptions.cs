namespace Lens.Core.DesenCodes;

/// <summary>
/// Desen kodu servisinin cagri sozlesmesi.
///
/// <c>GetDesenKodu</c> / <c>DosyaAdi</c> / <c>http://tempuri.org/</c>
/// 2026-09-11'de hedef ucun WSDL'inde CANLI dogrulandi, bu yuzden repo'daki
/// ornek dosyada ARTIK DOLU gelir. Yine de koda GOMULMEZ: servis tarafi bir
/// gun metot/parametre adini degistirirse yeniden derleme degil, tek satirlik
/// bir ayar degisikligi yeterli olmalidir.
///
/// <see cref="Endpoint"/> ise repo'da BOS kalir - gercek ortam adresi bir
/// altyapi bilgisidir ve kaynak koda/ornek ayara YAZILMAZ; kurulum/dagitim
/// ayarina IT tarafindan girilir (bkz. docs/DECISIONS.md #41).
///
/// Kimlik bilgisi alani BILEREK YOKTUR: uc, kimliksiz cagriya HTTP 200
/// donmektedir ve VPN girisi ile servis kimlik dogrulamasi AYNI SEY DEGILDIR.
/// </summary>
public sealed class DesenCodeServiceOptions
{
    /// <summary>
    /// Tam .asmx adresi. Repo'daki ornek ayarda BILEREK BOSTUR - kuruluma/
    /// dagitim ayarina IT girer. Bos ise servis yapilandirilmamis sayilir:
    /// kod guncelleme islemi calismaz, ama uygulama acilisi, arama,
    /// indeksleme ve kayitli kodlarin gosterimi normal devam eder.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>SOAP metot adi. WSDL'de dogrulanan deger: <c>GetDesenKodu</c>.</summary>
    public string? MethodName { get; set; }

    /// <summary>Metodun tek string parametresinin adi. WSDL'de dogrulanan deger: <c>DosyaAdi</c>.</summary>
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

    /// <summary>
    /// Yapilandirma eksikse kullaniciya gosterilecek TEK mesaj. Hangi alanin
    /// GERCEKTEN eksik oldugunu sayar - uc alanin tamamini saymak, yalnizca
    /// adresi girmesi gereken kullaniciyi yaniltirdi.
    /// </summary>
    public string DescribeMissingConfiguration()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            missing.Add("Endpoint (servis adresi)");
        }

        if (string.IsNullOrWhiteSpace(MethodName))
        {
            missing.Add("MethodName");
        }

        if (string.IsNullOrWhiteSpace(ParameterName))
        {
            missing.Add("ParameterName");
        }

        return "Desen kodu servisi yapılandırılmamış. Uygulama klasöründeki appsettings.json "
            + "dosyasında DesenCodeService bölümünde şu alan(lar) doldurulmalıdır: "
            + (missing.Count > 0 ? string.Join(", ", missing) : "Endpoint, MethodName, ParameterName")
            + ".";
    }
}
