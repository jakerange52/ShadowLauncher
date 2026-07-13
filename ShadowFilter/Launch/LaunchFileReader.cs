using ShadowFilter.Paths;

namespace ShadowFilter.Launch;

internal sealed class LaunchInfo
{
    public const string MasterFileVersion = "1.2";
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

    public bool IsRecentLaunch => DateTime.UtcNow - LaunchTimeUtc < TimeSpan.FromMinutes(5);
}

internal static class LaunchFileReader
{
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
                    try { File.Delete(path); } catch { /* best effort */ }
                }
            }
        }
        catch
        {
            // Non-fatal.
        }
    }

    public static LaunchInfo Read(string serverName, string accountName)
    {
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(accountName))
            return new LaunchInfo();

        var info = ReadFile(FilterPaths.GetLaunchFilePath(serverName, accountName));
        if (info.IsValid)
            return info;

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
        catch
        {
            // Invalid launch file — treat as missing.
        }

        return info;
    }
}
