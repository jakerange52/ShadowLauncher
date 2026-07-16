using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.FileSystem;

/// <summary>
/// Persists a small JSON sidecar file for every active <see cref="GameSession"/> so
/// that session state survives a launcher restart.
///
/// Lifecycle:
///   <see cref="Write"/> is called by <see cref="ShadowLauncher.Services.GameSessions.GameSessionService"/>
///   immediately after a session is created.
///   <see cref="Delete"/> is called when the session is closed (game exited / killed).
///
/// On launcher startup, <see cref="ReadAll"/> returns whatever entries are still on disk.
/// <see cref="AppCoordinator"/> then re-adopts live PIDs into the in-memory service and
/// performs cleanup for any PIDs that are no longer running.
///
/// Files are written to: %LocalAppData%\ShadowLauncher\Sessions\{sessionId}.json
/// Relaunch minimized preference: %LocalAppData%\ShadowLauncher\Sessions\relaunch_{accountId}_{serverId}.json
/// </summary>
public class SessionJournal
{
    private readonly string _directory;
    private readonly ILogger<SessionJournal> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SessionJournal(IConfigurationProvider config, ILogger<SessionJournal> logger)
    {
        _directory = Path.Combine(config.DataDirectory, "Sessions");
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    /// <summary>Writes (or overwrites) the journal entry for <paramref name="session"/>.</summary>
    public void Write(GameSession session)
    {
        try
        {
            // Atomic write: stage to a temp file then move into place so a crash mid-write
            // can't leave a corrupt journal entry on disk.
            var path = EntryPath(session.Id);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(session, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write session journal for {Id}", session.Id);
        }
    }

    /// <summary>Removes the journal entry for <paramref name="sessionId"/> if it exists.</summary>
    public void Delete(string sessionId)
    {
        try
        {
            var path = EntryPath(sessionId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete session journal for {Id}", sessionId);
        }
    }

    /// <summary>
    /// Returns all sessions found on disk. Corrupt or unreadable files are deleted
    /// and skipped rather than propagated as errors.
    /// Relaunch preference sidecars (<c>relaunch_*.json</c>) are not session journals.
    /// </summary>
    public IReadOnlyList<GameSession> ReadAll()
    {
        var results = new List<GameSession>();
        try
        {
            foreach (var file in Directory.GetFiles(_directory, "*.json"))
            {
                var name = Path.GetFileName(file);
                // Preference sidecars share this folder; deserializing them as GameSession
                // yields ProcessId=0 (System Idle) and crashes reconcile with Access Denied.
                if (name.StartsWith("relaunch_", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var text = File.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<GameSession>(text, JsonOptions);
                    if (session is null || string.IsNullOrWhiteSpace(session.Id) || session.ProcessId <= 0)
                        continue;

                    results.Add(session);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Corrupt session journal entry — deleting: {File}", file);
                    try { File.Delete(file); } catch { /* best effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate session journal directory");
        }
        return results;
    }

    private string EntryPath(string sessionId) => Path.Combine(_directory, $"{sessionId}.json");

    private string RelaunchEntryPath(string accountId, string serverId) =>
        Path.Combine(_directory, $"relaunch_{SanitizeKey(accountId)}_{SanitizeKey(serverId)}.json");

    private static string SanitizeKey(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "unknown";

        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');

        return value;
    }

    /// <summary>
    /// Persists whether the last session for this account/server exited while minimized.
    /// Survives session journal deletion and launcher restarts for auto-relaunch.
    /// </summary>
    public void WriteRelaunchMinimized(string accountId, string serverId, bool wasMinimized)
    {
        try
        {
            var payload = new RelaunchMinimizedState
            {
                WasMinimized = wasMinimized,
                UpdatedUtc = DateTime.UtcNow
            };
            var path = RelaunchEntryPath(accountId, serverId);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write relaunch minimized state for {Account}/{Server}", accountId, serverId);
        }
    }

    public bool GetRelaunchWasMinimized(string accountId, string serverId)
    {
        try
        {
            var path = RelaunchEntryPath(accountId, serverId);
            if (!File.Exists(path))
                return false;

            var state = JsonSerializer.Deserialize<RelaunchMinimizedState>(File.ReadAllText(path), JsonOptions);
            return state?.WasMinimized == true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read relaunch minimized state for {Account}/{Server}", accountId, serverId);
            return false;
        }
    }

    private sealed class RelaunchMinimizedState
    {
        public bool WasMinimized { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}
