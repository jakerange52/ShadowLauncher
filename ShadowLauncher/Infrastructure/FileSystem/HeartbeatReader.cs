using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.GameSessions;

namespace ShadowLauncher.Infrastructure.FileSystem;

/// <summary>
/// Reads heartbeat status files written by ThwargFilter (Decal plugin).
/// ThwargFilter writes to: %AppData%\ThwargLauncher\Running\game_{pid}.txt
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

        try
        {
            // Read with FileShare.ReadWrite since ThwargFilter may be writing.
            // Open directly and let the not-found exceptions below stand in for an
            // existence check — avoids a redundant stat and the TOCTOU race.
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                text = await reader.ReadToEndAsync();
            }

            // Parse only the handful of fields we actually consume rather than
            // materializing a dictionary of every key in the file. EnumerateLines
            // is allocation-free and handles CRLF natively.
            string? fileVersion = null;
            string? actualCharacterName = null;
            string? characterName = null;
            string? uptimeSeconds = null;
            string? isOnlineStr = null;

            foreach (var line in text.AsSpan().EnumerateLines())
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0) continue;

                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();

                // Last occurrence wins, matching the prior dictionary overwrite semantics.
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
            }

            // Verify file version compatibility (accept 1.x heartbeat schema only).
            if (fileVersion is not null && !fileVersion.StartsWith("1."))
                return null;

            var resolvedCharacterName = actualCharacterName ?? characterName ?? string.Empty;

            var heartbeat = new HeartbeatData
            {
                CharacterName = resolvedCharacterName,
                UptimeSeconds = int.TryParse(uptimeSeconds, out var up) ? up : 0,
                Timestamp = DateTime.UtcNow
            };

            // Determine status from IsOnline field
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
}
