using System.Text.Json;
using Lens.Core.IO;

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

    public static UserSettings Load()
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
        catch
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        AppPaths.EnsureLocalDirectoriesExist();
        var json = JsonSerializer.Serialize(this);
        AtomicFileWriter.WriteAllText(AppPaths.UserSettingsFilePath, json);
    }
}
