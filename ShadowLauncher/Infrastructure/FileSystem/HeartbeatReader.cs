using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Paths;
using ShadowLauncher.Services.GameSessions;

namespace ShadowLauncher.Infrastructure.FileSystem;

/// <summary>
/// Reads heartbeat status files from ShadowFilter or ThwargFilter.
/// Prefers %LocalAppData%\ShadowLauncher\Running\, then falls back to
/// %AppData%\ThwargLauncher\Running\ so ThwargFilter-only installs still monitor.
/// </summary>
public class HeartbeatReader : IHeartbeatReader
{
    public async Task<HeartbeatData?> ReadHeartbeatAsync(int processId)
    {
        var data = await TryReadAsync(ShadowLauncherPaths.GetHeartbeatFilePath(processId));
        return data ?? await TryReadAsync(ShadowLauncherPaths.GetThwargFilterHeartbeatFilePath(processId));
    }

    private static async Task<HeartbeatData?> TryReadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var lastWriteUtc = File.GetLastWriteTimeUtc(path);

            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                text = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(text))
                return null;

            string? fileVersion = null;
            string? actualCharacterName = null;
            string? characterName = null;
            string? uptimeSeconds = null;
            string? isOnlineStr = null;
            string? teamList = null;

            foreach (var line in text.AsSpan().EnumerateLines())
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0) continue;

                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();

                if (key.Equals("FileVersion", StringComparison.OrdinalIgnoreCase))
                    fileVersion = value.ToString();
                else if (key.Equals("ActualCharacterName", StringComparison.OrdinalIgnoreCase))
                    actualCharacterName = value.ToString();
                else if (key.Equals("CharacterName", StringComparison.OrdinalIgnoreCase))
                    characterName = value.ToString();
                else if (key.Equals("UptimeSeconds", StringComparison.OrdinalIgnoreCase))
                    uptimeSeconds = value.ToString();
                else if (key.Equals("IsOnline", StringComparison.OrdinalIgnoreCase))
                    isOnlineStr = value.ToString();
                else if (key.Equals("TeamList", StringComparison.OrdinalIgnoreCase))
                    teamList = value.ToString();
            }

            if (fileVersion is not null && !fileVersion.StartsWith("1."))
                return null;

            // InGame from ActualCharacterName only — launch-file CharacterName is echoed while
            // stuck on account-in-use and must not count as in-world.
            var actual = NormalizeName(actualCharacterName);
            var intended = NormalizeName(characterName);
            var online = isOnlineStr is not null && bool.TryParse(isOnlineStr, out var on) && on;

            return new HeartbeatData
            {
                ActualCharacterName = actual,
                CharacterName = !string.IsNullOrEmpty(actual) ? actual : intended,
                TeamList = teamList ?? string.Empty,
                UptimeSeconds = int.TryParse(uptimeSeconds, out var up) ? up : 0,
                Timestamp = lastWriteUtc,
                Status = online
                    ? (!string.IsNullOrEmpty(actual) ? GameSessionStatus.InGame : GameSessionStatus.CharacterSelection)
                    : GameSessionStatus.LoginScreen
            };
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch { return null; }
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        if (string.Equals(name, "LoginNotComplete", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return name.Trim();
    }

    public static void DeleteHeartbeatFile(int processId)
    {
        TryDelete(ShadowLauncherPaths.GetHeartbeatFilePath(processId));
        TryDelete(ShadowLauncherPaths.GetThwargFilterHeartbeatFilePath(processId));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }
}
