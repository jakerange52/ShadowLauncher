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

    // Kill-disconnected: confirm real world entry (empty actual → real name) so launch-file
    // echo on the account-in-use dialog is not treated as InGame.
    private readonly Dictionary<string, WorldTrack> _world = [];
    private readonly HashSet<int> _watchedPids = [];
    private readonly HashSet<string> _seenHeartbeat = [];
    private readonly Dictionary<string, (GameSessionStatus Status, string Character, int Uptime)> _lastUiHeartbeat = [];

    private sealed class WorldTrack
    {
        public bool Primed;
        public bool SawEmptyActual;
        public bool Confirmed;
        public DateTime? LeftWorldAt;
    }

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
                var sessions = (await _sessionService.GetActiveSessionsAsync()).ToList();
                if (sessions.Count > 0)
                    _logger.LogDebug("Monitor loop: checking {Count} active session(s)", sessions.Count);

                foreach (var session in sessions)
                {
                    if (token.IsCancellationRequested) break;
                    if (!IsProcessRunning(session.ProcessId))
                        continue;

                    if (_watchedPids.Add(session.ProcessId))
                        _ = WatchForExitAsync(session.ProcessId, session.Id, token);

                    var heartbeat = await _heartbeatReader.ReadHeartbeatAsync(session.ProcessId);
                    if (heartbeat is not null)
                    {
                        _seenHeartbeat.Add(session.Id);
                        await _sessionService.RecordHeartbeatAsync(session.Id, heartbeat);
                        NotifyHeartbeatIfChanged(session.Id, heartbeat);

                        var track = Track(session, heartbeat);

                        if (track.Confirmed && heartbeat.Status == GameSessionStatus.InGame
                            && WindowFocusHelper.TryGetMinimizedState(session.ProcessId, out var minNow))
                            _sessionService.UpdateMinimizedState(session.Id, minNow);

                        if (track.Confirmed)
                            _windowPlacement.ProcessSession(session, heartbeat.Status);

                        if (_config.KillOnMissingHeartbeat
                            && TryGetNotInWorldElapsed(session, heartbeat, track, out var elapsed))
                        {
                            _logger.LogWarning(
                                "PID {Pid} not in world for {Elapsed}s (timeout: {Timeout}s, status {Status}).",
                                session.ProcessId, elapsed, _config.KillHeartbeatTimeoutSeconds, heartbeat.Status);
                            await KillSessionAsync(session, elapsed, _config.KillHeartbeatTimeoutSeconds);
                        }
                    }
                    else if (await TryKillMissingHeartbeatAsync(session))
                    {
                        // killed
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

    private WorldTrack Track(GameSession session, HeartbeatData heartbeat)
    {
        if (!_world.TryGetValue(session.Id, out var track))
            _world[session.Id] = track = new WorldTrack();

        var first = !track.Primed;
        track.Primed = true;

        if (!heartbeat.HasEnteredWorld)
        {
            track.SawEmptyActual = true;
            if (track.Confirmed)
            {
                track.Confirmed = false;
                track.LeftWorldAt ??= DateTime.UtcNow;
            }
            return track;
        }

        // Confirm only after seeing an empty actual first (real login). Launch-file echo on
        // account-in-use looks in-world immediately — ignore that unless this is a restore
        // of a session already older than the kill timeout.
        if (track.SawEmptyActual || (first && session.GetAliveSeconds() > _config.KillHeartbeatTimeoutSeconds))
        {
            track.Confirmed = true;
            track.LeftWorldAt = null;
        }

        return track;
    }

    private bool TryGetNotInWorldElapsed(
        GameSession session, HeartbeatData heartbeat, WorldTrack track, out int elapsed)
    {
        var timeout = _config.KillHeartbeatTimeoutSeconds;

        if (!track.Confirmed)
        {
            elapsed = session.GetAliveSeconds();
            return elapsed > timeout;
        }

        var preWorld = heartbeat.Status is GameSessionStatus.LoginScreen
            or GameSessionStatus.CharacterSelection
            or GameSessionStatus.Launching;

        if (!preWorld)
        {
            track.LeftWorldAt = null;
            elapsed = 0;
            return false;
        }

        track.LeftWorldAt ??= DateTime.UtcNow;
        elapsed = (int)(DateTime.UtcNow - track.LeftWorldAt.Value).TotalSeconds;
        return elapsed > timeout;
    }

    private async Task<bool> TryKillMissingHeartbeatAsync(GameSession session)
    {
        if (!_config.KillOnMissingHeartbeat || !IsProcessRunning(session.ProcessId))
            return false;

        var timeout = _config.KillHeartbeatTimeoutSeconds;

        // Never wrote a heartbeat — still kill after launch timeout.
        if (!_seenHeartbeat.Contains(session.Id))
        {
            var alive = session.GetAliveSeconds();
            if (alive <= timeout)
                return false;

            _logger.LogWarning(
                "PID {Pid} still launching with no heartbeat for {Elapsed}s (timeout: {Timeout}s).",
                session.ProcessId, alive, timeout);
            await KillSessionAsync(session, alive, timeout);
            return true;
        }

        var silence = (DateTime.UtcNow - session.LastHeartbeatTime).TotalSeconds;
        var previous = session.Status;
        if (silence > 5 && previous != GameSessionStatus.Hanging)
            session.Status = GameSessionStatus.Hanging;

        if (silence > timeout)
        {
            await KillSessionAsync(session, (int)silence, timeout);
            return true;
        }

        if (session.Status == GameSessionStatus.Hanging && previous != GameSessionStatus.Hanging)
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

        return false;
    }

    private async Task KillSessionAsync(GameSession session, int elapsedSeconds, int timeoutSeconds)
    {
        _logger.LogWarning(
            "Killing PID {Pid} — not in world for {Elapsed}s (timeout: {Timeout}s)",
            session.ProcessId, elapsedSeconds, timeoutSeconds);

        var wasMinimized = session.WasMinimized;
        ClearTracking(session.Id, session.ProcessId);

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

    private async Task WatchForExitAsync(int processId, string sessionId, CancellationToken token)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.EnableRaisingEvents = true;
            await process.WaitForExitAsync(token);
        }
        catch (ArgumentException) { }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WatchForExitAsync error for PID {Pid}", processId);
        }

        var session = await _sessionService.GetSessionByProcessIdAsync(processId);
        var wasMinimized = session?.WasMinimized ?? false;
        ClearTracking(sessionId, processId);

        if (session is null)
            return;

        var active = (await _sessionService.GetActiveSessionsAsync()).ToList();
        _gameLauncher.CleanupShadowFilterLaunchFileIfUnused(
            session.AccountName, session.ServerName, active, processId);
        HeartbeatReader.DeleteHeartbeatFile(processId);
        await _sessionService.CloseSessionAsync(sessionId);
        GameExited?.Invoke(this, new GameExitedEventArgs(processId, wasMinimized));
        _logger.LogDebug("Process {Pid} exited — session closed via WaitForExitAsync", processId);
    }

    private void ClearTracking(string sessionId, int processId)
    {
        _windowPlacement.ClearSession(processId);
        _watchedPids.Remove(processId);
        _lastUiHeartbeat.Remove(sessionId);
        _seenHeartbeat.Remove(sessionId);
        _world.Remove(sessionId);
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
