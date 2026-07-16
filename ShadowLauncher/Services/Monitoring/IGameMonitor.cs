using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.GameSessions;

namespace ShadowLauncher.Services.Monitoring;

public interface IGameMonitor
{
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync();
    Task<ProcessStatus?> GetProcessStatusAsync(int processId);

    event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;
    event EventHandler<GameExitedEventArgs>? GameExited;
}

public class ProcessStatus
{
    public bool IsRunning { get; set; }
    public long MemoryUsageBytes { get; set; }
    public TimeSpan Uptime { get; set; }
}

public class HeartbeatReceivedEventArgs(string sessionId, HeartbeatData data) : EventArgs
{
    public string SessionId { get; } = sessionId;
    public HeartbeatData Data { get; } = data;
}

public class GameExitedEventArgs(int processId, bool wasMinimized) : EventArgs
{
    public int ProcessId { get; } = processId;
    public bool WasMinimized { get; } = wasMinimized;
}
