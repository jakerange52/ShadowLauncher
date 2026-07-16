using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Launching;

public interface IGameLauncher
{
    Task<LaunchResult> LaunchGameAsync(Account account, Server server);
    Task<bool> IsGameProcessRunningAsync(int processId);
    void CleanupShadowFilterLaunchFileIfUnused(
        string accountName,
        string serverName,
        IEnumerable<GameSession> activeSessions,
        int exceptProcessId);
}

public class LaunchResult
{
    public bool Success { get; set; }
    public int ProcessId { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
