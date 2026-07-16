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
                        PluginLog.Info(LogCategory, $"Deleted stale launch file: {Path.GetFileName(path)}");
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warn(LogCategory, $"Failed to delete stale launch file: {path}", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, "DeleteStaleLaunchFiles failed", ex);
        }
    }

    public static LaunchInfo Read(string serverName, string accountName)
    {
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(accountName))
            return new LaunchInfo();

        var path = FilterPaths.GetLaunchFilePath(serverName, accountName);
        var info = ReadFile(path);
        if (info.IsValid)
        {
            PluginLog.Info(LogCategory,
                $"Launch file valid for {serverName}/{accountName}, character={info.CharacterName}");
            return info;
        }

        if (File.Exists(path))
            PluginLog.Info(LogCategory, $"Launch file stale or invalid: {Path.GetFileName(path)}");
        else
            PluginLog.Info(LogCategory, $"No launch file at {Path.GetFileName(path)}");

        // Exact path only — never scan the launch folder; with multiple clients a scan
        // can return another account's file (most recent wins).
        return info;
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
                return info;
            }

            info.LaunchTimeUtc = KeyValueFile.ParseTimestamp(settings);
            if (DateTime.UtcNow - info.LaunchTimeUtc >= MaxLatency)
                return info;

            info.ServerName = KeyValueFile.GetString(settings, "ServerName");
            info.AccountName = KeyValueFile.GetString(settings, "AccountName");
            info.CharacterName = KeyValueFile.GetString(settings, "CharacterName");
            info.IsValid = true;
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, $"Failed to read launch file: {path}", ex);
        }

        return info;
    }
}
