using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.GameSessions;

public class GameSessionService : IGameSessionService
{
    private readonly Dictionary<string, GameSession> _sessions = [];
    private readonly ILogger<GameSessionService> _logger;

    public GameSessionService(ILogger<GameSessionService> logger)
    {
        _logger = logger;
    }

    public Task<GameSession> CreateSessionAsync(Account account, Server server, int processId)
    {
        var session = new GameSession
        {
            Id = Guid.NewGuid().ToString(),
            AccountId = account.Id,
            AccountName = account.Name,
            ServerId = server.Id,
            ServerName = server.Name,
            ProcessId = processId,
            Status = GameSessionStatus.Launching,
            StartTime = DateTime.UtcNow,
            LastHeartbeatTime = DateTime.UtcNow
        };

        _sessions[session.Id] = session;
        _logger.LogInformation("Session created: {Id} for account {Account} on {Server}, PID {Pid}",
            session.Id, account.Name, server.Name, processId);
        return Task.FromResult(session);
    }

    public Task UpdateSessionAsync(GameSession session)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<GameSession?> GetSessionAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<GameSession?> GetSessionByProcessIdAsync(int processId)
    {
        var session = _sessions.Values.FirstOrDefault(s => s.ProcessId == processId);
        return Task.FromResult(session);
    }

    public Task CloseSessionAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Status = GameSessionStatus.Offline;
            _sessions.Remove(sessionId);
            _logger.LogInformation("Session closed: {Id}", sessionId);
        }
        return Task.CompletedTask;
    }

    public async Task RecordHeartbeatAsync(string sessionId, HeartbeatData heartbeatData)
    {
        var session = await GetSessionAsync(sessionId);
        if (session is null) return;

        session.LastHeartbeatTime = heartbeatData.Timestamp;
        session.CharacterName = heartbeatData.CharacterName;
        session.Status = heartbeatData.Status;
        session.UptimeSeconds = heartbeatData.UptimeSeconds;
    }

    public async Task<bool> IsSessionAliveAsync(string sessionId, TimeSpan timeout)
    {
        var session = await GetSessionAsync(sessionId);
        if (session is null) return false;
        return (DateTime.UtcNow - session.LastHeartbeatTime) < timeout;
    }

    public Task<IEnumerable<GameSession>> GetActiveSessionsAsync()
    {
        var active = _sessions.Values
            .Where(s => s.Status is not GameSessionStatus.Offline and not GameSessionStatus.Exiting)
            .ToList();
        return Task.FromResult<IEnumerable<GameSession>>(active);
    }
}
