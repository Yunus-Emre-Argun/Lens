using System.Text.Json;
using Lens.Core.DesenCodes;
using Lens.Core.Logging;

namespace Lens.Core.Config;

/// <summary>
/// Exe ile ayni klasordeki appsettings.json'dan okunan, salt-okunur admin
/// varsayilani. Uygulama bu dosyaya asla yazmaz - IT/sistem yoneticisi
/// elle duzenler. Repo'daki ornek dosya bos gelir (gercek path hardcode
/// edilmez).
/// </summary>
public sealed class AdminConfig
{
    public string? AdminDefaultProductDirectory { get; set; }

    /// <summary>
    /// [Desen kodu servisi] Kod sorgulama ucunun sozlesmesi. Bolum yoksa veya
    /// alanlari bossa servis YAPILANDIRILMAMIS sayilir: "Desen Kodlarını
    /// Güncelle" islemi calismaz, ama uygulama acilisi, gorsel arama,
    /// indeksleme ve kayitli kodlarin gosterimi normal calismaya devam eder.
    /// Servise ARAMA SIRASINDA hicbir istek gonderilmez.
    ///
    /// Metot/parametre adlari BILEREK yapilandirmadadir - gorevde verilen
    /// metot adi hedef ucun WSDL'inde bulunamadi, dogru ad ogrenildiginde
    /// yeniden derleme gerekmemelidir (bkz. docs/DESEN_CODE_SERVICE.md).
    ///
    /// Bu dosyaya KIMLIK BILGISI YAZILMAZ - servis kimliksiz cagriya yanit
    /// vermektedir ve VPN girisi servis kimlik dogrulamasi DEGILDIR.
    /// </summary>
    public DesenCodeServiceOptions? DesenCodeService { get; set; }

    public static AdminConfig Load(string filePath, ILensLogger? logger = null)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new AdminConfig();
            }

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<AdminConfig>(json) ?? new AdminConfig();
        }
        catch (Exception ex)
        {
            // Bozuk/okunamayan config, uygulamayi crash ettirmemeli -
            // varsayilan yoksa kullanici manuel klasor secer. Yine de
            // (varsa) logger'a bildirilir ki destek ekibi fark edebilsin.
            logger?.Warning("AdminConfigLoad", file: filePath, reason: ex.Message);
            return new AdminConfig();
        }
    }
}
