using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.FileSystem;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Services.GameSessions;
using ShadowLauncher.Services.Launching;

namespace ShadowLauncher.Services.Monitoring;

public class GameMonitor : IGameMonitor
{
    private readonly IGameSessionService _sessionService;
    private readonly IHeartbeatReader _heartbeatReader;
    private readonly IConfigurationProvider _config;
    private readonly ILogger<GameMonitor> _logger;
    private readonly IGameLauncher _gameLauncher;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    public event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;
    public event EventHandler<GameExitedEventArgs>? GameExited;

    private readonly Dictionary<int, bool> _minimizedStates = [];

    // Tracks the first time a session was observed at the login screen *after* having
    // been in-game. Used to treat "Connection to the server has been lost" (which the
    // ThwargFilter reports as IsOnline=false / LoginScreen, indistinguishable from the
    // real login screen) as a disconnect for the Kill-disconnected-client setting.
    private readonly Dictionary<string, (DateTime Since, bool WasEverInGame)> _disconnectTracking = [];

    // Tracks PIDs that already have a WaitForExitAsync watcher running so we
    // don't spin up duplicates each time the monitor loop iterates.
    private readonly HashSet<int> _watchedPids = [];

    public GameMonitor(
        IGameSessionService sessionService,
        IHeartbeatReader heartbeatReader,
        IConfigurationProvider config,
        IGameLauncher gameLauncher,
        ILogger<GameMonitor> logger)
    {
        _sessionService = sessionService;
        _heartbeatReader = heartbeatReader;
        _config = config;
        _gameLauncher = gameLauncher;
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
                    _logger.LogDebug("Monitor loop: checking {Count} active session(s)", sessionList.Count);

                foreach (var session in sessionList)
                {
                    if (token.IsCancellationRequested) break;

                    // Start a zero-overhead exit watcher for this PID if not already running.
                    // WaitForExitAsync fires the moment the OS signals process termination —
                    // no polling delay. The loop below handles heartbeat/state only.
                    if (_watchedPids.Add(session.ProcessId))
                        _ = WatchForExitAsync(session.ProcessId, session.Id, token);

                    // Update minimized state while process is alive.
                    // Only overwrite when a window currently exists — otherwise a window that
                    // vanishes during shutdown would clobber a previously-captured "true".
                    if (WindowFocusHelper.TryGetMinimizedState(session.ProcessId, out var minNow))
                        _minimizedStates[session.ProcessId] = minNow;

                    // Try to read heartbeat from ThwargFilter/ShadowFilter
                    var heartbeat = await _heartbeatReader.ReadHeartbeatAsync(session.ProcessId);

                    if (heartbeat is not null)
                    {
                        await _sessionService.RecordHeartbeatAsync(session.Id, heartbeat);
                        HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEventArgs(session.Id, heartbeat));

                        // Track in-game → login-screen transition as a disconnect signal.
                        // ThwargFilter cannot distinguish the post-disconnect "Connection lost"
                        // dialog from the real login screen, so we infer it: if a session was
                        // ever in-game and is now back on the login screen, treat continued time
                        // there as a missing heartbeat for the kill timer.
                        var sticky = _disconnectTracking.GetValueOrDefault(session.Id);
                        if (heartbeat.Status == GameSessionStatus.InGame)
                        {
                            _disconnectTracking[session.Id] = (DateTime.MinValue, true);
                        }
                        else if (heartbeat.Status == GameSessionStatus.LoginScreen && sticky.WasEverInGame)
                        {
                            if (sticky.Since == DateTime.MinValue)
                                _disconnectTracking[session.Id] = (DateTime.UtcNow, true);

                            if (_config.KillOnMissingHeartbeat)
                            {
                                var since = _disconnectTracking[session.Id].Since;
                                var disconnectedFor = (DateTime.UtcNow - since).TotalSeconds;
                                var timeout = _config.KillHeartbeatTimeoutSeconds;
                                if (disconnectedFor > timeout)
                                {
                                    _logger.LogWarning(
                                        "PID {Pid} appears disconnected (login screen for {Elapsed}s after being in-game).",
                                        session.ProcessId, (int)disconnectedFor);
                                    await KillSessionAsync(session, (int)disconnectedFor, timeout);
                                    continue;
                                }
                            }
                        }
                    }
                    else
                    {
                        var elapsed = (DateTime.UtcNow - session.LastHeartbeatTime).TotalSeconds;

                        if (elapsed > 5)
                            session.Status = GameSessionStatus.Hanging;

                        if (_config.KillOnMissingHeartbeat)
                        {
                            var timeout = _config.KillHeartbeatTimeoutSeconds;
                            if (elapsed > timeout)
                            {
                                await KillSessionAsync(session, (int)elapsed, timeout);
                                continue;
                            }
                        }

                        var status = await GetProcessStatusAsync(session.ProcessId);
                        var synthetic = new HeartbeatData
                        {
                            CharacterName = session.CharacterName,
                            Status = session.Status == GameSessionStatus.Hanging
                                ? GameSessionStatus.Hanging
                                : GameSessionStatus.InGame,
                            UptimeSeconds = (int)(status?.Uptime.TotalSeconds ?? 0),
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

    private async Task KillSessionAsync(GameSession session, int elapsedSeconds, int timeoutSeconds)
    {
        _logger.LogWarning(
            "Killing PID {Pid} — no heartbeat for {Elapsed}s (timeout: {Timeout}s)",
            session.ProcessId, elapsedSeconds, timeoutSeconds);

        var wasMinimized = _minimizedStates.TryGetValue(session.ProcessId, out var m) && m;
        _minimizedStates.Remove(session.ProcessId);
        _watchedPids.Remove(session.ProcessId);
        _disconnectTracking.Remove(session.Id);

        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(session.ProcessId);
            proc.Kill(entireProcessTree: true);
        }
        catch { }

        await _sessionService.CloseSessionAsync(session.Id);
        GameExited?.Invoke(this, new GameExitedEventArgs(session.ProcessId, wasMinimized));
    }

    /// <summary>
    /// Waits for the process to exit using OS-level event notification (zero polling cost),
    /// then immediately fires <see cref="GameExited"/>. This runs concurrently with the
    /// heartbeat loop so exit detection is instant regardless of the 3-second poll interval.
    /// </summary>
    private async Task WatchForExitAsync(int processId, string sessionId, CancellationToken token)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.EnableRaisingEvents = true;
            await process.WaitForExitAsync(token);
        }
        catch (ArgumentException)
        {
            // Process was already gone by the time we opened it — treat as exited below.
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WatchForExitAsync error for PID {Pid}", processId);
        }

        _minimizedStates.TryGetValue(processId, out var wasMinimized);
        _minimizedStates.Remove(processId);
        _watchedPids.Remove(processId);

        // Only fire if the session is still active (heartbeat kill path may have beaten us).
        var session = await _sessionService.GetSessionByProcessIdAsync(processId);
        if (session is not null)
        {
            _disconnectTracking.Remove(session.Id);
            _gameLauncher.CleanupThwargFilterLaunchFile(session.AccountName, session.ServerName);
            await _sessionService.CloseSessionAsync(sessionId);
            GameExited?.Invoke(this, new GameExitedEventArgs(processId, wasMinimized));
            _logger.LogInformation("Process {Pid} exited — session closed immediately via WaitForExitAsync", processId);
        }
    }
}
