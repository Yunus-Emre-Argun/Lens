using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lens.Core.Config;

/// <summary>
/// Lens'in local (paylasilmayan) verilerinin tutuldugu yerler:
/// %LocalAppData%\Lens\{config,cache,logs}\. Urun dizini (UNC/mapped drive)
/// yalnizca gorsel icerir; Lens'e ozel teknik dosyalar buraya asla yazilmaz.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "Lens";
    private const string AdminConfigFileName = "appsettings.json";
    private const string UserSettingsFileName = "user-settings.json";
    private const string CacheMetaFileName = "meta.json";
    private const string CacheIndexFileName = "index.json";

    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string ConfigDirectory => Path.Combine(RootDirectory, "config");
    public static string CacheRootDirectory => Path.Combine(RootDirectory, "cache");
    public static string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public static string UserSettingsFilePath => Path.Combine(ConfigDirectory, UserSettingsFileName);

    /// <summary>Admin default config, exe ile ayni klasorde (publish ciktisinda IT tarafindan elle duzenlenir).</summary>
    public static string AdminConfigFilePath => Path.Combine(AppContext.BaseDirectory, AdminConfigFileName);

    public static void EnsureLocalDirectoriesExist()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(CacheRootDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>
    /// Verilen urun dizini icin deterministic bir cache alt klasoru dondurur
    /// (path hash'i). Ayni dizine tekrar donuldugunde ayni cache kullanilir;
    /// farkli dizinler birbirini ezmez. Klasor + kucuk bir meta.json (kaynak
    /// path'i insan-okunabilir tutmak icin) bu cagrida olusturulur.
    /// </summary>
    public static string EnsureCacheDirectoryFor(string productDirectory)
    {
        var cacheDir = Path.Combine(CacheRootDirectory, HashProductDirectory(productDirectory));
        Directory.CreateDirectory(cacheDir);
        WriteMetaFileIfMissing(cacheDir, productDirectory);
        return cacheDir;
    }

    public static string CacheIndexFilePath(string productDirectory) =>
        Path.Combine(EnsureCacheDirectoryFor(productDirectory), CacheIndexFileName);

    private static string HashProductDirectory(string productDirectory)
    {
        var normalized = NormalizeForHashing(productDirectory);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    private static string NormalizeForHashing(string path)
    {
        // GetFullPath salt lexical normalizasyon yapar, diske erismez -
        // erisilemeyen bir UNC path icin de guvenle cagrilabilir.
        var full = Path.GetFullPath(path);
        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.ToLowerInvariant();
    }

    private static void WriteMetaFileIfMissing(string cacheDir, string sourceProductDirectory)
    {
        var metaPath = Path.Combine(cacheDir, CacheMetaFileName);
        if (File.Exists(metaPath))
        {
            return;
        }

        try
        {
            var meta = JsonSerializer.Serialize(new { SourceProductDirectory = sourceProductDirectory });
            File.WriteAllText(metaPath, meta);
        }
        catch
        {
            // meta.json yalnizca support/debug kolayligi icindir; yazilamazsa
            // indexleme islevini engellemez.
        }
    }
}
