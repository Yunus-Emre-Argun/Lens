using System.Text.Json;

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

    public static AdminConfig Load(string filePath)
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
        catch
        {
            // Bozuk/okunamayan config, uygulamayi crash ettirmemeli -
            // varsayilan yoksa kullanici manuel klasor secer.
            return new AdminConfig();
        }
    }
}
