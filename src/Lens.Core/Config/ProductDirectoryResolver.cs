using Lens.Core.Logging;

namespace Lens.Core.Config;

public enum ProductDirectorySource
{
    UserOverride,
    AdminDefault,
    None,
}

public sealed record ProductDirectoryResolution(
    string? Directory,
    ProductDirectorySource Source,
    bool IsAccessible);

/// <summary>
/// Uygulama acilisinda hangi urun dizininin yuklenecegini belirler:
/// UserOverride (aktifse) &gt; AdminDefault &gt; hicbiri. Erisilebilirlik
/// kontrolu burada yapilir; cagiran taraf (UI) sonucu yorumlar.
/// </summary>
public static class ProductDirectoryResolver
{
    public static ProductDirectoryResolution ResolveDefault(ILensLogger? logger = null)
    {
        var userSettings = UserSettings.Load(logger);
        if (userSettings.UseUserOverride && !string.IsNullOrWhiteSpace(userSettings.UserOverrideProductDirectory))
        {
            var dir = userSettings.UserOverrideProductDirectory!;
            return new ProductDirectoryResolution(dir, ProductDirectorySource.UserOverride, IsAccessible(dir));
        }

        var adminConfig = AdminConfig.Load(AppPaths.AdminConfigFilePath, logger);
        if (!string.IsNullOrWhiteSpace(adminConfig.AdminDefaultProductDirectory))
        {
            var dir = adminConfig.AdminDefaultProductDirectory!;
            return new ProductDirectoryResolution(dir, ProductDirectorySource.AdminDefault, IsAccessible(dir));
        }

        return new ProductDirectoryResolution(null, ProductDirectorySource.None, false);
    }

    /// <summary>Kullanicinin sectigi klasoru kalici varsayilan yapar.</summary>
    public static void SetUserOverride(string productDirectory, ILensLogger? logger = null)
    {
        var settings = UserSettings.Load(logger);
        settings.UserOverrideProductDirectory = productDirectory;
        settings.UseUserOverride = true;
        settings.Save(logger);
    }

    /// <summary>Kullanici override'ini temizler; sonraki acilista admin default'a donulur.</summary>
    public static void ClearUserOverride(ILensLogger? logger = null)
    {
        var settings = UserSettings.Load(logger);
        settings.UseUserOverride = false;
        settings.Save(logger);
    }

    public static bool IsAccessible(string directory)
    {
        try
        {
            return Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }
}
