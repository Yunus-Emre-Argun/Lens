using System.Text.Json;
using Lens.Core.IO;
using Lens.Core.Logging;
using Lens.Core.Search;

namespace Lens.Core.Config;

/// <summary>
/// %LocalAppData%\Lens\config\user-settings.json altinda tutulan, kullanici
/// tarafindan degistirilebilir ayarlar. Kullanicinin bir oturumda gecici
/// olarak sectigi klasor buraya YAZILMAZ - yalnizca "Bu klasoru varsayilan
/// olarak kullan" ile acikca kalici hale getirilirse yazilir.
/// </summary>
public sealed class UserSettings
{
    public string? UserOverrideProductDirectory { get; set; }
    public bool UseUserOverride { get; set; }

    /// <summary>
    /// "Arama öncesi indeksi otomatik kontrol et ve güncelle" checkbox tercihi.
    /// Varsayilan acik (true) - eski (bu alani icermeyen) bir settings
    /// dosyasi yuklendiginde System.Text.Json bu alana dokunmaz, bu yuzden
    /// deserialize sonrasi da true kalir (geriye uyumlu "acik" davranis).
    /// </summary>
    public bool AutoIndexBeforeSearch { get; set; } = true;

    /// <summary>
    /// [Görsel güncelleme - tema turu] Kullanicinin ana pencere arka plan
    /// tercihi ("Acik"/"Normal"/"Koyu"/"AcikSepya"/"KoyuSepya"/"Lime" - bkz.
    /// Lens.Desktop.AppTheme). Salt UI tercihidir, shared index/urun
    /// klasorune YAZILMAZ ve baska bir kullaniciyi etkilemez - yalnizca bu
    /// bilgisayarin LocalAppData'sindaki bu dosyada tutulur.
    /// [2026-09-08] Varsayilan "Lime" (onceden "Normal" idi - bkz.
    /// docs/DECISIONS.md, SUPERSEDES eski "Normal varsayilandir" karari);
    /// eski (bu alani icermeyen) bir dosya yuklendiginde de
    /// AutoIndexBeforeSearch ile ayni geriye-uyumlu mantikla "Lime" kabul
    /// edilir. Bilinmeyen/gecersiz/bos bir deger MainWindow.ParseTheme
    /// tarafindan guvenle "Lime"a cevrilir - bu alanin kendisi hicbir
    /// dogrulama yapmaz (dumduz string). ONEMLI: bu yalnizca ALAN
    /// bulunmadigi/gecersiz oldugu durum icin gecerlidir - kullanicinin
    /// daha once ACIKCA kaydettigi GECERLI bir deger (ör. "Normal", "Koyu")
    /// oldugu gibi KORUNUR, bu varsayilan onu asla EZMEZ.
    /// </summary>
    public string Theme { get; set; } = "Lime";

    /// <summary>
    /// [Sonuç sınırı - kullanıcı tercihi] "En fazla sonuç" girdisinin kalıcı
    /// hali (bkz. MaxResultsPreference, MainWindow.MaxResultsTextBox).
    /// Varsayılan 15; eski (bu alanı içermeyen) bir dosya yüklendiğinde
    /// AutoIndexBeforeSearch/Theme ile aynı geriye-uyumlu mantıkla 15 kabul
    /// edilir. Yalnızca GEÇERLİ (1-999) bir değer arama başlatılırken
    /// kaydedilir (bkz. MainWindow.SearchButton_Click) - bu alanın kendisi
    /// hiçbir doğrulama yapmaz (dümdüz int); bozuk/aralık dışı bir kayıtlı
    /// değer MainWindow tarafında MaxResultsPreference.ValidateOrDefault ile
    /// güvenle 15'e çevrilir.
    /// </summary>
    public int PreferredMaxResults { get; set; } = MaxResultsPreference.Default;

    /// <summary>
    /// [Cok modelli arama] Secili model ("DinoV2Base" / "ClipStandard").
    /// Eski (bu alani icermeyen) bir dosya yuklendiginde KANITLANMIS
    /// varsayilan olan DINOv2-Base kullanilir - kullanicinin dogrulanmis
    /// davranisi ayar dosyasi eskidiginde de korunur. Bilinmeyen/gecersiz
    /// deger de guvenle ayni varsayilana doner (bkz.
    /// SearchModelCatalog.ResolveOrDefault).
    /// </summary>
    public string SearchModel { get; set; } = nameof(Lens.Core.Ai.SearchModelKind.DinoV2Base);

    /// <summary>[Cok modelli arama] Goruntu degerlendirme modu ("Color" / "Grayscale"). Varsayilan renkli - kanitlanmis profil.</summary>
    public string ImageColorMode { get; set; } = nameof(Lens.Core.Ai.ImageColorMode.Color);

    /// <summary>
    /// [Cok modelli arama] "Desen odakli karsilastirma" (embedding merkezleme
    /// ve yeniden normallestirme) acik mi. Varsayilan KAPALI - kanitlanmis
    /// DINO renkli davranisi degismesin diye.
    /// </summary>
    public bool PatternFocusedComparison { get; set; }

    /// <summary>
    /// [Cok modelli arama] Model + renk modu + merkezleme kombinasyonu basina
    /// kullanicinin en son kullandigi minimum benzerlik esigi.
    ///
    /// Neden kombinasyon basina? Her yontemin skor dagilimi FARKLIDIR; tek
    /// bir esigi tum yontemlerde kullanmak, kullanicinin bir yontemde
    /// ayarladigi degeri digerinde anlamsiz hale getirirdi. Anahtar bicimi
    /// icin bkz. SearchModelProfile.ThresholdKey.
    ///
    /// Eski bir dosyada bu alan yoksa bos sozluk olusur ve her kombinasyon
    /// kendi olculmus varsayilaniyla baslar.
    /// </summary>
    public Dictionary<string, double> ThresholdByProfile { get; set; } = new();

    public static UserSettings Load(ILensLogger? logger = null)
    {
        try
        {
            var path = AppPaths.UserSettingsFilePath;
            if (!File.Exists(path))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            logger?.Warning("UserSettingsLoad", file: AppPaths.UserSettingsFilePath, reason: ex.Message);
            return new UserSettings();
        }
    }

    public void Save(ILensLogger? logger = null)
    {
        try
        {
            AppPaths.EnsureLocalDirectoriesExist();
            var json = JsonSerializer.Serialize(this);
            AtomicFileWriter.WriteAllText(AppPaths.UserSettingsFilePath, json);
        }
        catch (Exception ex)
        {
            // [Faz 4C] Onceden bu metod hata firlatiyordu (try/catch yoktu).
            // Diger config islemleriyle tutarli olsun ve tek basina bir
            // ayar-kaydetme sorunu tum uygulamayi cokertmesin diye
            // yakalanip loglaniyor.
            logger?.Error("UserSettingsSave", file: AppPaths.UserSettingsFilePath, reason: ex.Message);
        }
    }
}
