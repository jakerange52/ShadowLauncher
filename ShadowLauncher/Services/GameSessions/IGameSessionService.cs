using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.GameSessions;

public interface IGameSessionService
{
    Task<GameSession> CreateSessionAsync(Account account, Server server, int processId);
    Task<GameSession?> GetSessionAsync(string sessionId);
    Task<GameSession?> GetSessionByProcessIdAsync(int processId);
    Task CloseSessionAsync(string sessionId);
    Task RecordHeartbeatAsync(string sessionId, HeartbeatData heartbeatData);
    Task<IEnumerable<GameSession>> GetActiveSessionsAsync();

    /// <summary>Restores a session recovered from the on-disk journal into the in-memory store.</summary>
    Task RestoreSessionAsync(GameSession session);
}

public class HeartbeatData
{
    public string CharacterName { get; set; } = string.Empty;
    public GameSessionStatus Status { get; set; }
    public int UptimeSeconds { get; set; }
    public string TeamList { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
