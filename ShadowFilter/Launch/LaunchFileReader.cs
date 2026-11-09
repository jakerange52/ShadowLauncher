using ShadowFilter.Paths;

namespace ShadowFilter.Launch;

internal sealed class LaunchInfo
{
    public const string MasterFileVersionCompat = "1";

    public bool IsValid { get; set; }
    public string FileVersion { get; set; } = string.Empty;
    public DateTime LaunchTimeUtc { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;

    public bool HasValidCharacterName =>
        !string.IsNullOrEmpty(CharacterName) &&
        !string.Equals(CharacterName, "None", StringComparison.OrdinalIgnoreCase);

    public string InvalidReason { get; set; } = string.Empty;
}

internal static class LaunchFileReader
{
    private const string LogCategory = nameof(LaunchFileReader);
    private static readonly TimeSpan MaxLatency = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Removes launch files older than the staleness window so a Decal-tray or
    /// manual client start does not pick up a leftover auto-login character.
    /// </summary>
    public static void DeleteStaleLaunchFiles()
    {
        try
        {
            var folder = FilterPaths.LaunchFilesFolder;
            if (!Directory.Exists(folder))
                return;

            foreach (var path in Directory.GetFiles(folder, $"launch_{ShadowFilterPluginIds.FilterName}_*.txt"))
            {
                var info = ReadFile(path);
                if (!info.IsValid)
                {
                    try
                    {
                        File.Delete(path);
                        PluginLog.Info(LogCategory,
                            $"Deleted stale launch file: {Path.GetFileName(path)} ({info.InvalidReason})");
                    }
                    catch (Exception ex)
                    {
                        OomGuard.DieIfOom(ex);
                        PluginLog.Warn(LogCategory, $"Failed to delete stale launch file: {path}", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            OomGuard.DieIfOom(ex);
            PluginLog.Warn(LogCategory, "DeleteStaleLaunchFiles failed", ex);
        }
    }

    public static LaunchInfo Read(string serverName, string accountName)
    {
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(accountName))
            return new LaunchInfo();

        var path = ResolveLaunchFilePath(serverName, accountName);
        var info = ReadFile(path);
        if (info.IsValid)
        {
            PluginLog.Info(LogCategory,
                $"Launch file valid for {serverName}/{accountName}, character={info.CharacterName}");
            return info;
        }

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            PluginLog.Info(LogCategory,
                $"Launch file stale or invalid: {Path.GetFileName(path)} ({info.InvalidReason})");
        else
            PluginLog.Info(LogCategory, $"No launch file at launch_{ShadowFilterPluginIds.FilterName}_{serverName}_{accountName}.txt");

        // Name match only. Do not pick the newest file in the folder — that can be
        // another account when two clients launch together.
        return info;
    }

    /// <summary>
    /// The client reports the account in lowercase (elrek). The launcher writes the
    /// account as stored (Elrek). Match the file name, not a different account.
    /// </summary>
    private static string ResolveLaunchFilePath(string serverName, string accountName)
    {
        var expected = $"launch_{ShadowFilterPluginIds.FilterName}_{serverName}_{accountName}.txt";
        var direct = Path.Combine(FilterPaths.LaunchFilesFolder, expected);
        if (File.Exists(direct))
            return direct;

        var folder = FilterPaths.LaunchFilesFolder;
        if (!Directory.Exists(folder))
            return direct;

        foreach (var path in Directory.GetFiles(folder, $"launch_{ShadowFilterPluginIds.FilterName}_*.txt"))
        {
            if (string.Equals(Path.GetFileName(path), expected, StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return direct;
    }

    private static LaunchInfo ReadFile(string path)
    {
        var info = new LaunchInfo();
        try
        {
            if (!File.Exists(path))
                return info;

            var settings = KeyValueFile.Parse(path);
            info.FileVersion = KeyValueFile.GetString(settings, "FileVersion");
            if (string.IsNullOrEmpty(info.FileVersion) ||
                !info.FileVersion.StartsWith(LaunchInfo.MasterFileVersionCompat, StringComparison.Ordinal))
            {
                info.InvalidReason = string.IsNullOrEmpty(info.FileVersion)
                    ? "missing FileVersion"
                    : $"FileVersion {info.FileVersion}";
                return info;
            }

            // The launcher writes Timestamp=TimeUtc:'...'. If that line does not parse,
            // the file write time is the same clock the 45s window is meant to use.
            // A fresh file must not be deleted as stale — that drops auto-login.
            var parsed = KeyValueFile.ParseTimestamp(settings);
            if (parsed != DateTime.MinValue)
                info.LaunchTimeUtc = parsed;
            else
                info.LaunchTimeUtc = File.GetLastWriteTimeUtc(path);

            if (info.LaunchTimeUtc == DateTime.MinValue)
            {
                info.InvalidReason = "no timestamp";
                return info;
            }

            var age = DateTime.UtcNow - info.LaunchTimeUtc;
            if (age >= MaxLatency || age < TimeSpan.FromMinutes(-2))
            {
                info.InvalidReason = parsed == DateTime.MinValue
                    ? $"file age {age.TotalSeconds:0}s (timestamp did not parse)"
                    : $"age {age.TotalSeconds:0}s";
                return info;
            }

            info.ServerName = KeyValueFile.GetString(settings, "ServerName");
            info.AccountName = KeyValueFile.GetString(settings, "AccountName");
            info.CharacterName = KeyValueFile.GetString(settings, "CharacterName");
            info.IsValid = true;
        }
        catch (Exception ex)
        {
            OomGuard.DieIfOom(ex);
            PluginLog.Warn(LogCategory, $"Failed to read launch file: {path}", ex);
        }

        return info;
    }
}
