using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.GameSessions;

public interface IGameSessionService
{
    Task<GameSession> CreateSessionAsync(Account account, Server server, int processId);
    Task<GameSession?> GetSessionByProcessIdAsync(int processId);
    GameSession? FindSessionByProcessId(int processId);
    void UpdateMinimizedState(string sessionId, bool wasMinimized);
    bool GetRelaunchWasMinimized(string accountId, string serverId);
    Task CloseSessionAsync(string sessionId);
    Task RecordHeartbeatAsync(string sessionId, HeartbeatData heartbeatData);
    Task<IEnumerable<GameSession>> GetActiveSessionsAsync();

    /// <summary>Restores a session recovered from the on-disk journal into the in-memory store.</summary>
    Task RestoreSessionAsync(GameSession session);
}

public class HeartbeatData
{
    public string CharacterName { get; set; } = string.Empty;
    public string ActualCharacterName { get; set; } = string.Empty;
    public string TeamList { get; set; } = string.Empty;
    public GameSessionStatus Status { get; set; }
    public int UptimeSeconds { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public bool HasEnteredWorld => !string.IsNullOrWhiteSpace(ActualCharacterName);
}
