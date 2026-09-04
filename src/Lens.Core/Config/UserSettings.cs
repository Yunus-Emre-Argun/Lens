using System.Text.Json;
using Lens.Core.IO;
using Lens.Core.Logging;

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
    /// bilgisayarin LocalAppData'sindaki bu dosyada tutulur. Varsayilan
    /// "Normal"; eski (bu alani icermeyen) bir dosya yuklendiginde de
    /// AutoIndexBeforeSearch ile ayni geriye-uyumlu mantikla "Normal" kabul
    /// edilir. Bilinmeyen/gecersiz bir deger MainWindow.ParseTheme
    /// tarafindan guvenle "Normal"e cevrilir - bu alanin kendisi hicbir
    /// dogrulama yapmaz (dumduz string).
    /// </summary>
    public string Theme { get; set; } = "Normal";

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
