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
    public int ServerMonitorPort { get; set; }
    public GameSessionStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastHeartbeatTime { get; set; }
    public int UptimeSeconds { get; set; }
}

public enum GameSessionStatus
{
    Launching,
    LoginScreen,
    CharacterSelection,
    InGame,
    Hanging,
    Exiting,
    Offline
}
