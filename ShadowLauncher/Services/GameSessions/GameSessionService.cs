using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.FileSystem;

namespace ShadowLauncher.Services.GameSessions;

public class GameSessionService : IGameSessionService
{
    private readonly Dictionary<string, GameSession> _sessions = [];
    private readonly SessionJournal _journal;
    private readonly ILogger<GameSessionService> _logger;

    public GameSessionService(SessionJournal journal, ILogger<GameSessionService> logger)
    {
        _journal = journal;
        _logger = logger;
    }

    public Task<GameSession> CreateSessionAsync(Account account, Server server, int processId)
    {
        var startedAtUtc = DateTime.UtcNow;
        var session = new GameSession
        {
            Id = Guid.NewGuid().ToString(),
            AccountId = account.Id,
            AccountName = account.Name,
            ServerId = server.Id,
            ServerName = server.Name,
            ProcessId = processId,
            Status = GameSessionStatus.Launching,
            StartedAtUtc = startedAtUtc,
            LastHeartbeatTime = startedAtUtc
        };

        _sessions[session.Id] = session;
        _journal.Write(session);
        _logger.LogInformation("Session created: {Id} for account {Account} on {Server}, PID {Pid}",
            session.Id, account.Name, server.Name, processId);
        return Task.FromResult(session);
    }

    private Task<GameSession?> GetSessionAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public GameSession? FindSessionByProcessId(int processId) =>
        _sessions.Values.FirstOrDefault(s => s.ProcessId == processId);

    public Task CloseSessionAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _journal.WriteRelaunchMinimized(session.AccountId, session.ServerId, session.WasMinimized);
            session.Status = GameSessionStatus.Offline;
            _sessions.Remove(sessionId);
            _journal.Delete(sessionId);
            _logger.LogInformation(
                "Session closed: {Id} (wasMinimized: {WasMinimized})",
                sessionId, session.WasMinimized);
        }
        return Task.CompletedTask;
    }

    public void UpdateMinimizedState(string sessionId, bool wasMinimized)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;

        if (session.WasMinimized == wasMinimized)
            return;

        session.WasMinimized = wasMinimized;
        _journal.Write(session);
    }

    public bool GetRelaunchWasMinimized(string accountId, string serverId) =>
        _journal.GetRelaunchWasMinimized(accountId, serverId);

    public async Task RecordHeartbeatAsync(string sessionId, HeartbeatData heartbeatData)
    {
        var session = await GetSessionAsync(sessionId);
        if (session is null) return;

        session.LastHeartbeatTime = heartbeatData.Timestamp;
        session.CharacterName = heartbeatData.CharacterName;
        session.Status = heartbeatData.Status;
        session.UptimeSeconds = session.GetAliveSeconds();
    }

    public Task<IEnumerable<GameSession>> GetActiveSessionsAsync()
    {
        var active = _sessions.Values
            .Where(s => s.Status is not GameSessionStatus.Offline and not GameSessionStatus.Exiting)
            .ToList();
        return Task.FromResult<IEnumerable<GameSession>>(active);
    }

    /// <summary>
    /// Directly inserts a <see cref="GameSession"/> that was restored from the journal.
    /// Skips the journal write because the entry is already on disk.
    /// </summary>
    public Task RestoreSessionAsync(GameSession session)
    {
        _sessions[session.Id] = session;
        _logger.LogDebug("Session restored from journal: {Id} PID {Pid}", session.Id, session.ProcessId);
        return Task.CompletedTask;
    }
}
