using System.Reflection;

namespace Lens.Desktop;

/// <summary>
/// [Sürüm bilgisi - 2026-09-07] Gösterilen sürüm metninin TEK kaynağı - XAML/C#
/// içinde SABİT bir metin YOK. Değer, derlenen `Lens.Desktop.exe`'nin Assembly
/// metadata'sından (<see cref="AssemblyInformationalVersionAttribute"/>) okunur;
/// bu attribute, `Lens.Desktop.csproj`'daki `InformationalVersion` MSBuild
/// özelliğinden (Visual Studio: Project Properties/Assembly Information ekranı)
/// SDK tarafından OTOMATİK üretilir - burada elle YAZILMAZ. Alt bilgi satırı
/// (bkz. MainWindow.xaml "FooterVersionText") ve Hakkında ekranı (bkz.
/// MainWindow.AboutMenuItem_Click) AYNI bu metodu çağırır - iki farklı yerde
/// tekrar okuma mantığı YOKTUR, dolayısıyla ikisi asla farklı sürüm gösteremez.
/// </summary>
internal static class AppVersionInfo
{
    private const string FallbackText = "Sürüm bilgisi yok";

    /// <summary>
    /// Kullanıcıya gösterilecek sürüm metnini döner (ör. "08.09.26 — v1.0").
    /// Assembly metadata herhangi bir nedenle okunamaz/boşsa uygulama ÇÖKMEZ -
    /// önce sayısal AssemblyVersion'a (eski Hakkında ekranının TEK kaynağıydı,
    /// artık yalnızca SON ÇARE yedeği), o da yoksa <see cref="FallbackText"/>'e
    /// güvenle düşer.
    /// </summary>
    public static string GetDisplayVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informational))
            {
                return informational;
            }

            var version = assembly.GetName().Version;
            return version is null ? FallbackText : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return FallbackText;
        }
    }
}
