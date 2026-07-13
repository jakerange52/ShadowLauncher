using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Paths;
using ShadowLauncher.Services.GameSessions;

namespace ShadowLauncher.Infrastructure.FileSystem;

/// <summary>
/// Reads heartbeat status files written by ShadowFilter (Decal plugin).
/// ShadowFilter writes to: %LocalAppData%\ShadowLauncher\Running\game_{pid}.txt
/// </summary>
public class HeartbeatReader : IHeartbeatReader
{
    public string GetHeartbeatFilePath(int processId)
        => ShadowLauncherPaths.GetHeartbeatFilePath(processId);

    public async Task<HeartbeatData?> ReadHeartbeatAsync(int processId)
    {
        var path = GetHeartbeatFilePath(processId);

        try
        {
            if (!File.Exists(path))
                return null;

            var lastWriteUtc = File.GetLastWriteTimeUtc(path);

            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                text = await reader.ReadToEndAsync();
            }

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

            var resolvedCharacterName = actualCharacterName ?? characterName ?? string.Empty;

            var heartbeat = new HeartbeatData
            {
                CharacterName = resolvedCharacterName,
                TeamList = teamList ?? string.Empty,
                UptimeSeconds = int.TryParse(uptimeSeconds, out var up) ? up : 0,
                // Prefer file write time so kill timers measure real silence, not "now".
                Timestamp = lastWriteUtc
            };

            if (isOnlineStr is not null && bool.TryParse(isOnlineStr, out var isOnline) && isOnline)
            {
                heartbeat.Status = !string.IsNullOrEmpty(resolvedCharacterName)
                    ? GameSessionStatus.InGame
                    : GameSessionStatus.CharacterSelection;
            }
            else
            {
                heartbeat.Status = GameSessionStatus.LoginScreen;
            }

            return heartbeat;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static void DeleteHeartbeatFile(int processId)
    {
        try
        {
            var path = ShadowLauncherPaths.GetHeartbeatFilePath(processId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }
}
