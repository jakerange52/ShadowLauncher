using Microsoft.Extensions.Logging;

namespace ShadowLauncher.Infrastructure.Logging;

/// <summary>
/// A simple file logger that writes to daily log files and cleans up files older than 7 days.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly int _retentionDays;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private StreamWriter? _writer;
    private string _currentDate = string.Empty;

    public FileLoggerProvider(string logDirectory, int retentionDays = 7)
    {
        _logDirectory = logDirectory;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(logDirectory);
        CleanOldLogs();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void WriteLog(string categoryName, LogLevel level, string message, Exception? exception)
    {
        var now = DateTime.Now;
        var date = now.ToString("yyyy-MM-dd");

        _lock.Wait();
        try
        {
            // Roll to new file on new day
            if (date != _currentDate)
            {
                _writer?.Dispose();
                _currentDate = date;
                var path = Path.Combine(_logDirectory, $"ShadowLauncher_{date}.log");
                _writer = new StreamWriter(path, append: true) { AutoFlush = true };
            }

            var timestamp = now.ToString("HH:mm:ss.fff");
            var levelStr = level switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???"
            };

            // Shorten category name to just the class name
            var category = categoryName.Contains('.')
                ? categoryName[(categoryName.LastIndexOf('.') + 1)..]
                : categoryName;

            _writer?.WriteLine($"[{timestamp}] [{levelStr}] [{category}] {message}");
            if (exception is not null)
                _writer?.WriteLine($"  Exception: {exception}");
        }
        finally
        {
            _lock.Release();
        }
    }

    private void CleanOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);
            foreach (var file in Directory.GetFiles(_logDirectory, "ShadowLauncher_*.log"))
            {
                // Parse the date from the filename (ShadowLauncher_yyyy-MM-dd.log) so that
                // external file touches (antivirus, indexing, etc.) don't extend retention.
                var name = Path.GetFileNameWithoutExtension(file);
                var datePart = name["ShadowLauncher_".Length..];
                if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null,
                        System.Globalization.DateTimeStyles.None, out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Don't fail startup over log cleanup
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _lock.Dispose();
    }
}

internal sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        provider.WriteLog(categoryName, logLevel, formatter(state, exception), exception);
    }
}
