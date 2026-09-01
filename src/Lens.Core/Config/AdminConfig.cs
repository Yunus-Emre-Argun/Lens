using System.Text.Json;
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
