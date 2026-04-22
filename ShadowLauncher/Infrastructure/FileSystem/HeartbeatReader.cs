using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.GameSessions;

namespace ShadowLauncher.Infrastructure.FileSystem;

/// <summary>
/// Reads heartbeat status files written by ThwargFilter (Decal plugin).
/// ThwargFilter writes to: %AppData%\ThwargLauncher\Running\gameToLauncher_{pid}.txt
/// Format is line-based Key:Value pairs.
/// </summary>
public class HeartbeatReader : IHeartbeatReader
{
    private static readonly string ThwargRunningFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ThwargLauncher", "Running");

    public string GetHeartbeatFilePath(int processId)
        => Path.Combine(ThwargRunningFolder, $"game_{processId}.txt");

    public async Task<HeartbeatData?> ReadHeartbeatAsync(int processId)
    {
        var path = GetHeartbeatFilePath(processId);
        if (!File.Exists(path))
            return null;

        try
        {
            // Read with FileShare.ReadWrite since ThwargFilter may be writing
            string[] lines;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                var text = await reader.ReadToEndAsync();
                lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0) continue;
                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();
                dict[key] = value;
            }

            // Verify file version compatibility
            if (dict.TryGetValue("FileVersion", out var version) && !version.StartsWith("1"))
                return null;

            var heartbeat = new HeartbeatData
            {
                CharacterName = dict.GetValueOrDefault("ActualCharacterName",
                    dict.GetValueOrDefault("CharacterName", string.Empty)),
                UptimeSeconds = int.TryParse(dict.GetValueOrDefault("UptimeSeconds", "0"), out var up) ? up : 0,
                TeamList = dict.GetValueOrDefault("TeamList", string.Empty),
                Timestamp = DateTime.UtcNow
            };

            // Determine status from IsOnline field
            if (dict.TryGetValue("IsOnline", out var onlineStr)
                && bool.TryParse(onlineStr, out var isOnline) && isOnline)
            {
                heartbeat.Status = !string.IsNullOrEmpty(heartbeat.CharacterName)
                    ? GameSessionStatus.InGame
                    : GameSessionStatus.CharacterSelection;
            }
            else
            {
                heartbeat.Status = GameSessionStatus.LoginScreen;
            }

            return heartbeat;
        }
        catch
        {
            return null;
        }
    }
}
