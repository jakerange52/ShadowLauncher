using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.FileSystem;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Services.GameSessions;

namespace ShadowLauncher.Services.Monitoring;

public class GameMonitor : IGameMonitor
{
    private readonly IGameSessionService _sessionService;
    private readonly IHeartbeatReader _heartbeatReader;
    private readonly IConfigurationProvider _config;
    private readonly ILogger<GameMonitor> _logger;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    public event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;
    public event EventHandler<GameExitedEventArgs>? GameExited;

    private readonly Dictionary<int, bool> _minimizedStates = [];

    public GameMonitor(
        IGameSessionService sessionService,
        IHeartbeatReader heartbeatReader,
        IConfigurationProvider config,
        ILogger<GameMonitor> logger)
    {
        _sessionService = sessionService;
        _heartbeatReader = heartbeatReader;
        _config = config;
        _logger = logger;
    }

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = MonitorLoopAsync(_cts.Token);
        _logger.LogInformation("Game monitoring started");
        return Task.CompletedTask;
    }

    public async Task StopMonitoringAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_monitoringTask is not null)
            {
                try { await _monitoringTask; } catch (OperationCanceledException) { }
            }
            _cts.Dispose();
            _cts = null;
        }
        _logger.LogInformation("Game monitoring stopped");
    }

    public async Task<HeartbeatStatus?> GetHeartbeatStatusAsync(int processId)
    {
        var session = await _sessionService.GetSessionByProcessIdAsync(processId);
        if (session is null) return null;

        var heartbeat = await _heartbeatReader.ReadHeartbeatAsync(processId);
        if (heartbeat is null) return null;

        return new HeartbeatStatus
        {
            IsResponding = true,
            LastHeartbeat = heartbeat.Timestamp,
            SecondsSinceLastHeartbeat = (int)(DateTime.UtcNow - heartbeat.Timestamp).TotalSeconds,
            CurrentCharacter = heartbeat.CharacterName
        };
    }

    public Task<ProcessStatus?> GetProcessStatusAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return Task.FromResult<ProcessStatus?>(new ProcessStatus
            {
                IsRunning = !process.HasExited,
                MemoryUsageBytes = process.WorkingSet64,
                Uptime = DateTime.Now - process.StartTime
            });
        }
        catch (ArgumentException)
        {
            return Task.FromResult<ProcessStatus?>(null);
        }
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var sessions = await _sessionService.GetActiveSessionsAsync();
                var sessionList = sessions.ToList();
                if (sessionList.Count > 0)
                    _logger.LogInformation("Monitor loop: checking {Count} active session(s)", sessionList.Count);

                foreach (var session in sessionList)
                {
                    if (token.IsCancellationRequested) break;

                    // Check if process is still running
                    var status = await GetProcessStatusAsync(session.ProcessId);
                    if (status is null || !status.IsRunning)
                    {
                        // Process genuinely gone — use last known minimized state
                        _minimizedStates.TryGetValue(session.ProcessId, out var wasMinimized);
                        _minimizedStates.Remove(session.ProcessId);
                        await _sessionService.CloseSessionAsync(session.Id);
                        GameExited?.Invoke(this, new GameExitedEventArgs(session.ProcessId, wasMinimized));
                        continue;
                    }

                    // Update minimized state while process is alive
                    _minimizedStates[session.ProcessId] = WindowFocusHelper.IsMinimized(session.ProcessId);

                    // Try to read heartbeat from ThwargFilter/ShadowFilter
                    var heartbeat = await _heartbeatReader.ReadHeartbeatAsync(session.ProcessId);

                    if (heartbeat is not null)
                    {
                        // We have real in-game data from the filter plugin
                        await _sessionService.RecordHeartbeatAsync(session.Id, heartbeat);
                        HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEventArgs(session.Id, heartbeat));
                    }
                    else
                    {
                        // Kill on missing heartbeat if enabled and session has been alive long enough
                        var elapsed = (DateTime.UtcNow - session.LastHeartbeatTime).TotalSeconds;

                        // Always mark as hanging after 5s regardless of kill toggle
                        if (elapsed > 5)
                            session.Status = GameSessionStatus.Hanging;

                        if (_config.KillOnMissingHeartbeat)
                        {
                            var timeout = _config.KillHeartbeatTimeoutSeconds;
                            if (elapsed > timeout)
                            {
                                _logger.LogWarning(
                                    "Killing PID {Pid} — no heartbeat for {Elapsed}s (timeout: {Timeout}s)",
                                    session.ProcessId, (int)elapsed, timeout);
                                var wasMinimizedBeforeKill = _minimizedStates.TryGetValue(session.ProcessId, out var m) && m;
                                _minimizedStates.Remove(session.ProcessId);
                                try
                                {
                                    using var proc = System.Diagnostics.Process.GetProcessById(session.ProcessId);
                                    proc.Kill(entireProcessTree: true);
                                }
                                catch { }
                                await _sessionService.CloseSessionAsync(session.Id);
                                GameExited?.Invoke(this, new GameExitedEventArgs(session.ProcessId, wasMinimizedBeforeKill));
                                continue;
                            }
                        }

                        // No filter heartbeat file — synthesize from process uptime
                        var synthetic = new HeartbeatData
                        {
                            CharacterName = session.CharacterName,
                            Status = session.Status == GameSessionStatus.Hanging
                                ? GameSessionStatus.Hanging
                                : GameSessionStatus.InGame,
                            UptimeSeconds = (int)status.Uptime.TotalSeconds,
                            Timestamp = DateTime.UtcNow
                        };
                        HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEventArgs(session.Id, synthetic));
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(3), token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in game monitoring loop");
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
        }
    }
}
