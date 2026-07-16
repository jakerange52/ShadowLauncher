using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Channels;
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
    private readonly ChannelRelayService _channelRelay;
    private readonly GameWindowPlacementService _windowPlacement;
    private CancellationTokenSource? _cts;
    private Task? _monitoringTask;

    public event EventHandler<HeartbeatReceivedEventArgs>? HeartbeatReceived;
    public event EventHandler<GameExitedEventArgs>? GameExited;

    // Tracks the first time a session was observed at the login screen *after* having
    // been in-game. Used to treat "Connection to the server has been lost" (which
    // ShadowFilter reports as IsOnline=false / LoginScreen, indistinguishable from the
    // real login screen) as a disconnect for the Kill-disconnected-client setting.
    private readonly Dictionary<string, (DateTime Since, bool WasEverInGame)> _disconnectTracking = [];

    // Tracks PIDs that already have a WaitForExitAsync watcher running so we
    // don't spin up duplicates each time the monitor loop iterates.
    private readonly HashSet<int> _watchedPids = [];

    // Sessions that have received at least one real ShadowFilter heartbeat.
    // Missing-heartbeat kill must not run before the plugin has had a chance to write.
    private readonly HashSet<string> _seenHeartbeat = [];

    // Last heartbeat status pushed to the UI per session — avoids flooding the
    // dispatcher when ShadowFilter has not written a file yet (common during multi-launch).
    private readonly Dictionary<string, (GameSessionStatus Status, string Character, int Uptime)> _lastUiHeartbeat = [];

    public GameMonitor(
        IGameSessionService sessionService,
        IHeartbeatReader heartbeatReader,
        IConfigurationProvider config,
        IGameLauncher gameLauncher,
        ChannelRelayService channelRelay,
        GameWindowPlacementService windowPlacement,
        ILogger<GameMonitor> logger)
    {
        _sessionService = sessionService;
        _heartbeatReader = heartbeatReader;
        _config = config;
        _gameLauncher = gameLauncher;
        _channelRelay = channelRelay;
        _windowPlacement = windowPlacement;
        _logger = logger;
    }

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = MonitorLoopAsync(_cts.Token);
        _logger.LogDebug("Game monitoring started");
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
        _logger.LogDebug("Game monitoring stopped");
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

                    if (!IsProcessRunning(session.ProcessId))
                        continue;

                    // Start a zero-overhead exit watcher for this PID if not already running.
                    // WaitForExitAsync fires the moment the OS signals process termination —
                    // no polling delay. The loop below handles heartbeat/state only.
                    if (_watchedPids.Add(session.ProcessId))
                        _ = WatchForExitAsync(session.ProcessId, session.Id, token);

                    // Sample minimized state only based on the just-read heartbeat (below).
                    // Sampling unconditionally here loses the value the moment the AC window
                    // is replaced by the un-minimized "Connection lost" dialog.

                    // Try to read heartbeat from ShadowFilter
                    var heartbeat = await _heartbeatReader.ReadHeartbeatAsync(session.ProcessId);

                    if (heartbeat is not null)
                    {
                        _seenHeartbeat.Add(session.Id);
                        await _sessionService.RecordHeartbeatAsync(session.Id, heartbeat);
                        NotifyHeartbeatIfChanged(session.Id, heartbeat);

                        // Only refresh the minimized snapshot while the client is healthily in-game.
                        // Once the heartbeat reports anything else (LoginScreen / CharacterSelection /
                        // Hanging) the window we'd be reading is no longer the game window, so the
                        // last in-game value must stick across disconnect → kill → relaunch.
                        if (heartbeat.Status == GameSessionStatus.InGame
                            && WindowFocusHelper.TryGetMinimizedState(session.ProcessId, out var minNow))
                            _sessionService.UpdateMinimizedState(session.Id, minNow);

                        _windowPlacement.ProcessSession(session, heartbeat.Status);

                        // Track in-game → login-screen transition as a disconnect signal.
                        // ShadowFilter cannot distinguish the post-disconnect "Connection lost"
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
                                if (disconnectedFor > timeout && IsProcessRunning(session.ProcessId))
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
                        // No file yet / unreadable — do not kill until ShadowFilter has written
                        // at least once. Startup and plugin-load lag are normal.
                        if (!_seenHeartbeat.Contains(session.Id))
                            continue;

                        var elapsed = (DateTime.UtcNow - session.LastHeartbeatTime).TotalSeconds;

                        var previousStatus = session.Status;
                        if (elapsed > 5 && previousStatus != GameSessionStatus.Hanging)
                            session.Status = GameSessionStatus.Hanging;

                        if (_config.KillOnMissingHeartbeat)
                        {
                            var timeout = _config.KillHeartbeatTimeoutSeconds;
                            if (elapsed > timeout && IsProcessRunning(session.ProcessId))
                            {
                                await KillSessionAsync(session, (int)elapsed, timeout);
                                continue;
                            }
                        }

                        // Only notify the UI when status degrades to Hanging — not on every poll
                        // while clients are still starting up and ShadowFilter has not written yet.
                        if (session.Status == GameSessionStatus.Hanging
                            && previousStatus != GameSessionStatus.Hanging)
                        {
                            var status = await GetProcessStatusAsync(session.ProcessId);
                            NotifyHeartbeatIfChanged(session.Id, new HeartbeatData
                            {
                                CharacterName = session.CharacterName,
                                Status = GameSessionStatus.Hanging,
                                UptimeSeconds = (int)(status?.Uptime.TotalSeconds ?? 0),
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }
                }

                await _channelRelay.ProcessActiveSessionsAsync(token);

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

        var wasMinimized = session.WasMinimized;
        _windowPlacement.ClearSession(session.ProcessId);
        _watchedPids.Remove(session.ProcessId);
        _lastUiHeartbeat.Remove(session.Id);
        _seenHeartbeat.Remove(session.Id);
        _disconnectTracking.Remove(session.Id);

        try
        {
            if (IsProcessRunning(session.ProcessId))
            {
                using var proc = Process.GetProcessById(session.ProcessId);
                proc.Kill(entireProcessTree: false);
            }
        }
        catch { }

        HeartbeatReader.DeleteHeartbeatFile(session.ProcessId);

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

        var session = await _sessionService.GetSessionByProcessIdAsync(processId);
        var wasMinimized = session?.WasMinimized ?? false;
        _windowPlacement.ClearSession(processId);
        _watchedPids.Remove(processId);
        _lastUiHeartbeat.Remove(sessionId);
        _seenHeartbeat.Remove(sessionId);

        if (session is not null)
        {
            _disconnectTracking.Remove(session.Id);
            var active = (await _sessionService.GetActiveSessionsAsync()).ToList();
            _gameLauncher.CleanupShadowFilterLaunchFileIfUnused(
                session.AccountName, session.ServerName, active, processId);
            HeartbeatReader.DeleteHeartbeatFile(processId);
            await _sessionService.CloseSessionAsync(sessionId);
            GameExited?.Invoke(this, new GameExitedEventArgs(processId, wasMinimized));
            _logger.LogDebug("Process {Pid} exited — session closed via WaitForExitAsync", processId);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void NotifyHeartbeatIfChanged(string sessionId, HeartbeatData heartbeat)
    {
        var key = (heartbeat.Status, heartbeat.CharacterName, heartbeat.UptimeSeconds);
        if (_lastUiHeartbeat.TryGetValue(sessionId, out var last) && last == key)
            return;

        _lastUiHeartbeat[sessionId] = key;
        HeartbeatReceived?.Invoke(this, new HeartbeatReceivedEventArgs(sessionId, heartbeat));
    }
}
