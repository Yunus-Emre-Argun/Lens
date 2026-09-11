using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;

namespace Lens.Core.DesenCodes;

/// <summary>
/// Klasik ASP.NET (.asmx) SOAP 1.1 istemcisi.
///
/// ⚠ CANLI OLARAK DOGRULANMADI. Gorevde verilen metot adi hedef ucun
/// WSDL'inde BULUNAMADI (92 metot tarandi). Bu sinif, AYNI SERVISIN
/// WSDL'inde GERCEK metotlar uzerinden dogrulanan cagri gelenegini uygular:
///
///   SOAPAction : &lt;Namespace&gt;&lt;MethodName&gt;
///   Istek      : &lt;MethodName&gt;&lt;ParameterName&gt;deger&lt;/...&gt;&lt;/MethodName&gt;
///   Cevap      : &lt;MethodNameResponse&gt;&lt;MethodNameResult&gt;metin&lt;/...&gt;&lt;/...&gt;
///
/// Yani sozlesme "tahmin" degil, ayni ucun kendi gelenegidir; ancak asil
/// metodun gercekte bu kalibi izleyecegi DOGRULANAMAMISTIR. Metot/parametre
/// adlari yapilandirmadan gelir (bkz. <see cref="DesenCodeServiceOptions"/>).
///
/// Guvenlik: kimlik bilgisi GONDERILMEZ. Uc, kimliksiz cagriya yanit
/// vermektedir ve VPN girisi ile servis kimlik dogrulamasi ayni sey degildir.
/// </summary>
public sealed class SoapDesenCodeService : IDesenCodeService, IDisposable
{
    private readonly DesenCodeServiceOptions _options;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    /// <param name="options">Uc/metot/parametre sozlesmesi. Yapilandirilmamissa <see cref="InvalidOperationException"/> firlatilir.</param>
    /// <param name="client">Istege bagli, disaridan yonetilen HTTP istemcisi (test icin). Verilmezse kendi istemcisini olusturur ve birakir.</param>
    public SoapDesenCodeService(DesenCodeServiceOptions options, HttpClient? client = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(options.DescribeMissingConfiguration());
        }

        _ownsClient = client is null;
        _client = client ?? new HttpClient();
        _client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
    }

    /// <inheritdoc />
    public async Task<DesenCodeLookupResult> LookupAsync(string fileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return DesenCodeLookupResult.Invalid("Sorgulanacak dosya adı boş.");
        }

        var primary = _options.SendFileExtension ? fileName : Path.GetFileNameWithoutExtension(fileName);
        var result = await QueryAsync(primary, cancellationToken).ConfigureAwait(false);

        // [Fallback - yalnizca ACIK "bulunamadi" durumunda] Baglanti hatasi,
        // zaman asimi veya SOAP fault durumunda ASLA ikinci deneme yapilmaz;
        // aksi halde ag sorunu iki katina cikar ve gecici hata kalici
        // "bulunamadi" gibi gorunurdu.
        if (result.Status == DesenCodeLookupStatus.NotFound
            && _options.SendFileExtension
            && _options.TryWithoutExtensionOnNotFound)
        {
            var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            if (!string.Equals(withoutExtension, primary, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(withoutExtension))
            {
                var second = await QueryAsync(withoutExtension, cancellationToken).ConfigureAwait(false);
                if (second.Status == DesenCodeLookupStatus.Found)
                {
                    return second;
                }
            }
        }

        return result;
    }

    private async Task<DesenCodeLookupResult> QueryAsync(string queryValue, CancellationToken cancellationToken)
    {
        string responseBody;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
            {
                Content = new StringContent(BuildEnvelope(queryValue), Encoding.UTF8, "text/xml"),
            };

            // ASMX SOAP 1.1: SOAPAction basligi zorunludur ve namespace +
            // metot adindan olusur (ayni WSDL'de dogrulandi).
            request.Content.Headers.ContentType!.CharSet = "utf-8";
            request.Headers.Add("SOAPAction", $"\"{CombineNamespace()}{_options.MethodName}\"");

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // SOAP fault genelde HTTP 500 ile gelir - govdedeki faultstring
                // varsa onu gosteririz, yoksa durum kodunu.
                var fault = TryReadFault(responseBody);
                return DesenCodeLookupResult.Unavailable(
                    fault ?? $"Servis {(int)response.StatusCode} {response.StatusCode} döndü.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Kullanicinin iptali - hata degil, yukari aynen tasinir.
            throw;
        }
        catch (TaskCanceledException ex)
        {
            // Iptal DEGIL, zaman asimi (HttpClient.Timeout).
            return DesenCodeLookupResult.Unavailable($"Zaman aşımı ({_options.TimeoutSeconds} sn): {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return DesenCodeLookupResult.Unavailable($"Servise ulaşılamadı: {ex.Message}");
        }
        catch (Exception ex)
        {
            return DesenCodeLookupResult.Unavailable($"Beklenmeyen ağ hatası: {ex.Message}");
        }

        return ParseResponse(responseBody, queryValue);
    }

    /// <summary>
    /// SOAP cevabindan YALNIZCA sonuc alanini cikarir. XML'in tamami asla kod
    /// olarak dondurulmez; beklenen &lt;MethodNameResult&gt; ogesi yoksa
    /// <see cref="DesenCodeLookupStatus.InvalidResponse"/> donulur.
    /// </summary>
    internal DesenCodeLookupResult ParseResponse(string responseBody, string queriedFileName)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(responseBody);
        }
        catch (Exception ex)
        {
            return DesenCodeLookupResult.Invalid($"Cevap XML olarak ayrıştırılamadı: {ex.Message}");
        }

        var fault = TryReadFault(responseBody);
        if (fault is not null)
        {
            return DesenCodeLookupResult.Unavailable(fault);
        }

        var resultName = _options.MethodName + "Result";
        var element = document.Descendants().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, resultName, StringComparison.Ordinal));

        if (element is null)
        {
            return DesenCodeLookupResult.Invalid($"Cevapta '{resultName}' alanı bulunamadı.");
        }

        // Bastaki/sondaki bosluklar temizlenir; ic degerin KENDISI (orn.
        // "00123") aynen korunur - bastaki sifirlar kesinlikle kirpilmaz,
        // sayiya CEVRILMEZ.
        var code = element.Value.Trim();

        // Bos cevap gercek bir kod DEGILDIR - "bu dosya icin kod yok"
        // anlamina gelir ve oyle kaydedilir.
        return string.IsNullOrEmpty(code)
            ? DesenCodeLookupResult.NotFound(queriedFileName)
            : DesenCodeLookupResult.Found(code, queriedFileName);
    }

    /// <summary>SOAP fault varsa faultstring'i dondurur; yoksa null.</summary>
    private static string? TryReadFault(string responseBody)
    {
        try
        {
            var document = XDocument.Parse(responseBody);
            var fault = document.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "Fault", StringComparison.Ordinal));
            if (fault is null)
            {
                return null;
            }

            var message = fault.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "faultstring", StringComparison.Ordinal)
                || string.Equals(e.Name.LocalName, "Text", StringComparison.Ordinal))?.Value;

            return $"Servis hatası: {(string.IsNullOrWhiteSpace(message) ? "SOAP fault" : message.Trim())}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Istek zarfi. Dosya adi <see cref="XElement"/> ile yazildigi icin
    /// &amp;, &lt;, &gt; gibi XML ozel karakterleri iceren adlar OTOMATIK
    /// olarak dogru kacislanir - elle string birlestirme yapilmaz.
    /// </summary>
    internal string BuildEnvelope(string queryValue)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace service = CombineNamespace();

        var envelope = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", soap.NamespaceName),
                new XElement(soap + "Body",
                    new XElement(service + _options.MethodName!,
                        new XElement(service + _options.ParameterName!, queryValue)))));

        return envelope.Declaration + Environment.NewLine + envelope;
    }

    /// <summary>Namespace sonundaki "/" tutarliligini saglar - SOAPAction'in dogru olusmasi buna baglidir.</summary>
    private string CombineNamespace()
    {
        var ns = string.IsNullOrWhiteSpace(_options.Namespace) ? "http://tempuri.org/" : _options.Namespace.Trim();
        return ns.EndsWith('/') ? ns : ns + "/";
    }

    /// <summary>Yalnizca KENDI olusturdugu HTTP istemcisini birakir - disaridan verilen istemciye dokunmaz.</summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
