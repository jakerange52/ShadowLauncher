namespace ShadowFilter;

internal static class PluginLog
{
    private static string? _logPath;

    public static string LogFilePath
    {
        get
        {
            if (_logPath != null)
                return _logPath;

            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            _logPath = Path.Combine(
                Paths.FilterPaths.AppFolder,
                "Logs",
                $"ShadowFilter_{pid}_log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            return _logPath;
        }
    }

    public static void WriteInfo(string message)
    {
        try
        {
            File.AppendAllText(LogFilePath, $"{DateTime.Now:o} INFO {message}{Environment.NewLine}");
        }
        catch { }
    }
}
