using System.Globalization;

namespace ShadowFilter;

internal static class PluginLog
{
    private const int RetentionDays = 7;
    private static readonly object Lock = new();
    private static string? _logPath;

    public static string LogFilePath
    {
        get
        {
            if (_logPath != null)
                return _logPath;

            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            _logPath = Path.Combine(
                Paths.FilterPaths.LogsFolder,
                $"ShadowFilter_{date}_{pid}.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            return _logPath;
        }
    }

    public static void CleanOldLogs()
    {
        try
        {
            var folder = Paths.FilterPaths.LogsFolder;
            if (!Directory.Exists(folder))
                return;

            var cutoff = DateTime.Now.Date.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(folder, "ShadowFilter_*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                // ShadowFilter_yyyy-MM-dd_pid
                var underscore = name.IndexOf('_');
                if (underscore < 0)
                    continue;

                var datePart = name.Substring(underscore + 1);
                var secondUnderscore = datePart.IndexOf('_');
                if (secondUnderscore > 0)
                    datePart = datePart.Substring(0, secondUnderscore);

                if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null, DateTimeStyles.None, out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Never throw from logging.
        }
    }

    public static void Info(string category, string message) => Write("INF", category, message);

    public static void Warn(string category, string message, Exception? ex = null) => Write("WRN", category, message, ex);

    public static void Error(string category, string message, Exception? ex = null) => Write("ERR", category, message, ex);

    public static void WriteInfo(string message) => Info("Manual", message);

    private static void Write(string level, string category, string message, Exception? ex = null)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var line = $"[{timestamp}] [{level}] [{category}] {message}";
            if (ex != null)
                line += $"{Environment.NewLine}  Exception: {ex}";

            lock (Lock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Never throw from logging.
        }
    }
}
