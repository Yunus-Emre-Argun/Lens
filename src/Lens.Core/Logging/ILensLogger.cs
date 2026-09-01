namespace Lens.Core.Logging;

/// <summary>
/// Minimal loglama soyutlamasi. Amac: ImageIndex/AdminConfig/UserSettings gibi
/// Core siniflarinin dosya-yazma detaylarina (path, format, retention) DEGIL,
/// yalnizca bu 3 metotluk anlamsal sozlesmeye bagimli olmasi (dusuk coupling).
/// Somut dosya-tabanli implementasyon FileLogger'dadir.
/// </summary>
public interface ILensLogger
{
    void Info(string operation, string? file = null, string? extension = null, string? reason = null);

    void Warning(string operation, string? file = null, string? extension = null, string? reason = null);

    void Error(string operation, string? file = null, string? extension = null, string? reason = null);
}
