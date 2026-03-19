using System.Runtime.CompilerServices;

namespace DaTT.App.Infrastructure;

/// <summary>
/// Thread-safe file logger. Writes to %AppData%\DaTT\logs\datt-YYYYMMDD.log.
/// Initialized once at startup via <see cref="Initialize"/>.
/// </summary>
public static class AppLog
{
    private static string _logPath = string.Empty;
    private static readonly object _lock = new();

    public static void Initialize()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DaTT", "logs");

        Directory.CreateDirectory(dir);

        _logPath = Path.Combine(dir, $"datt-{DateTime.Now:yyyyMMdd}.log");

        Info("──────────────────────────────────────────");
        Info($"DaTT started — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Info("──────────────────────────────────────────");
    }

    public static string LogPath => _logPath;

    public static void Info(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
        => Write("INFO ", message, null, member, file);

    public static void Warn(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
        => Write("WARN ", message, null, member, file);

    public static void Error(string message, Exception? ex = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
        => Write("ERROR", message, ex, member, file);

    private static void Write(string level, string message, Exception? ex,
        string member, string file)
    {
        var shortFile = Path.GetFileNameWithoutExtension(file);
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{shortFile}.{member}] {message}";

        if (ex is not null)
            line += Environment.NewLine + "  Exception: " + ex.ToString();

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch
            {
                // Log write failure must never crash the app
            }
        }

        System.Diagnostics.Debug.WriteLine(line);
    }
}
