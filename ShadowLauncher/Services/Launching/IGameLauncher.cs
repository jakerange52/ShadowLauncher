using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Launching;

public interface IGameLauncher
{
    Task<LaunchResult> LaunchGameAsync(Account account, Character character, Server server);
    Task TerminateGameAsync(int processId);
    Task<bool> IsGameProcessRunningAsync(int processId);
}

public class LaunchResult
{
    public bool Success { get; set; }
    public int ProcessId { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
}
