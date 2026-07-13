namespace ShadowLauncher.Core.Models;

public class GameSession
{
    public string Id { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public GameSessionStatus Status { get; set; }
    public DateTime LastHeartbeatTime { get; set; }

    /// <summary>UTC time the launcher created this session (persisted in the session journal).</summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>Last known minimized state while in-game (persisted in the session journal).</summary>
    public bool WasMinimized { get; set; }

    public int UptimeSeconds { get; set; }

    /// <summary>Elapsed seconds since <see cref="StartedAtUtc"/> (session journal anchor).</summary>
    public int GetAliveSeconds()
    {
        if (StartedAtUtc != default)
            return (int)Math.Max(0, (DateTime.UtcNow - StartedAtUtc).TotalSeconds);

        if (LastHeartbeatTime != default)
            return (int)Math.Max(0, (DateTime.UtcNow - LastHeartbeatTime).TotalSeconds);

        return UptimeSeconds;
    }
}

