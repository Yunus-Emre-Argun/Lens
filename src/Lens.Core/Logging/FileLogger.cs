using System.Text;
using Lens.Core.Config;

namespace Lens.Core.Logging;

/// <summary>
/// %LocalAppData%\Lens\logs\lens-yyyyMMdd.log altina yazan, gunluk-dosya
/// tabanli, duz UTF-8 metin logger. Tum dosya-yazma/format/retention
/// detaylari BURADA kapali tutulur - Core'un geri kalani yalnizca
/// ILensLogger arayuzunu bilir.
///
/// Loglama IKINCIL bir kaygidir: herhangi bir yazma/temizlik hatasi
/// sessizce yutulur, Lens'in indeksleme/arama islevini asla etkilemez.
/// </summary>
public sealed class FileLogger : ILensLogger
{
    private const int RetentionDays = 30;
    private readonly object _writeLock = new();

    public FileLogger()
    {
        PurgeOldLogs();
    }

    public void Info(string operation, string? file = null, string? extension = null, string? reason = null)
        => Write(LogLevel.Info, operation, file, extension, reason);

    public void Warning(string operation, string? file = null, string? extension = null, string? reason = null)
        => Write(LogLevel.Warning, operation, file, extension, reason);

    public void Error(string operation, string? file = null, string? extension = null, string? reason = null)
        => Write(LogLevel.Error, operation, file, extension, reason);

    private void Write(LogLevel level, string operation, string? file, string? extension, string? reason)
    {
        try
        {
            AppPaths.EnsureLocalDirectoriesExist();
            var logPath = Path.Combine(AppPaths.LogsDirectory, $"lens-{DateTime.Now:yyyyMMdd}.log");
            var line = FormatLine(level, operation, file, extension, reason);

            lock (_writeLock)
            {
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Ana islevi asla bozmaz - log satiri kaybolur, o kadar.
        }
    }

    private static string FormatLine(LogLevel level, string operation, string? file, string? extension, string? reason)
    {
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
        sb.Append(operation);

        if (!string.IsNullOrEmpty(file))
        {
            sb.Append(" | File: ").Append(file);
        }

        if (!string.IsNullOrEmpty(extension))
        {
            sb.Append(" | Format: ").Append(extension);
        }

        if (!string.IsNullOrEmpty(reason))
        {
            sb.Append(" | Reason: ").Append(reason);
        }

        return sb.ToString();
    }

    private static void PurgeOldLogs()
    {
        try
        {
            AppPaths.EnsureLocalDirectoriesExist();
            var cutoff = DateTime.Now.AddDays(-RetentionDays);

            foreach (var path in Directory.EnumerateFiles(AppPaths.LogsDirectory, "lens-*.log"))
            {
                if (File.GetLastWriteTime(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch
        {
            // Retention basarisiz olsa da uygulama acilisi engellenmez.
        }
    }
}
