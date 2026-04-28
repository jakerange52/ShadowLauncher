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
            File.WriteAllText(EntryPath(session.Id), JsonSerializer.Serialize(session, JsonOptions));
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
    /// </summary>
    public IReadOnlyList<GameSession> ReadAll()
    {
        var results = new List<GameSession>();
        try
        {
            foreach (var file in Directory.GetFiles(_directory, "*.json"))
            {
                try
                {
                    var text = File.ReadAllText(file);
                    var session = JsonSerializer.Deserialize<GameSession>(text, JsonOptions);
                    if (session is not null)
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
}
