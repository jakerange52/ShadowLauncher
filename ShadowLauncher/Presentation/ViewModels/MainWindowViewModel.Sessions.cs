using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Services.Monitoring;

namespace ShadowLauncher.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private async void OnGameExited(int processId, bool wasMinimized)
    {
        _logger.LogInformation("Game process exited: PID {Pid} (wasMinimized: {WasMinimized})", processId, wasMinimized);
        _pendingMinimizeOnInGame.Remove(processId);
        var session = ActiveSessions.FirstOrDefault(s => s.ProcessId == processId);
        if (session is not null)
        {
            ActiveSessions.Remove(session);
            StatusText = $"Session ended: {session.AccountName} on {session.ServerName}";
        }

        if (AutoRelaunch && _launchedSessions.TryGetValue(processId, out var info))
        {
            _launchedSessions.Remove(processId);

            var hasAliveSessionForCombo = ActiveSessions.Any(s =>
                string.Equals(s.AccountId, info.Account.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.ServerId, info.Server.Id, StringComparison.OrdinalIgnoreCase));

            if (hasAliveSessionForCombo)
            {
                _logger.LogInformation("Skipping auto-relaunch for {Account} on {Server} because an active session already exists.",
                    info.Account.Name, info.Server.Name);
                StatusText = $"Skipping relaunch for {info.Account.Name} on {info.Server.Name} (already active).";
                return;
            }

            _logger.LogInformation("Auto-relaunching {Account} on {Server}", info.Account.Name, info.Server.Name);
            StatusText = $"Auto-relaunching {info.Account.Name} on {info.Server.Name}...";

            try
            {
                await Task.Delay(AutoRelaunchDelaySeconds * 1000);

                var result = await _gameLauncher.LaunchGameAsync(info.Account, info.Server);
                if (result.Success)
                {
                    var newSession = await _sessionService.CreateSessionAsync(info.Account, info.Server, result.ProcessId);
                    ActiveSessions.Add(CloneSessionForUi(newSession));
                    _launchedSessions[result.ProcessId] = info;
                    StatusText = $"Auto-relaunched {info.Account.Name} (PID {result.ProcessId})";

                    if (_sessionService.GetRelaunchWasMinimized(info.Account.Id, info.Server.Id))
                    {
                        var character = _loginCommandsService.GetDefaultCharacter(info.Account.Name, info.Server.Name);
                        var hasAutoLoginCharacter = !string.IsNullOrWhiteSpace(character)
                            && !string.Equals(character, "any", StringComparison.OrdinalIgnoreCase);

                        if (hasAutoLoginCharacter)
                        {
                            _logger.LogDebug("Will re-minimize PID {Pid} after character '{Char}' reaches in-game",
                                result.ProcessId, character);
                            _pendingMinimizeOnInGame.Add(result.ProcessId);
                        }
                        else
                        {
                            _logger.LogDebug("Previous client was minimized — will re-minimize relaunched PID {Pid}", result.ProcessId);
                            _ = MinimizeWhenReadyAsync(result.ProcessId);
                        }
                    }
                }
                else
                {
                    StatusText = $"Auto-relaunch failed for {info.Account.Name}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-relaunch failed for {Account}", info.Account.Name);
                StatusText = $"Auto-relaunch error: {ex.Message}";
            }
        }
        else
        {
            _launchedSessions.Remove(processId);
        }
    }

    private async Task MinimizeWhenReadyAsync(int processId)
    {
        const int totalMs = 30_000;
        const int intervalMs = 500;
        var elapsed = 0;
        var everMinimized = false;
        while (elapsed < totalMs)
        {
            await Task.Delay(intervalMs);
            elapsed += intervalMs;

            var exited = await Task.Run(() =>
            {
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById(processId);
                    return p.HasExited;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            });
            if (exited)
                return;

            var minimized = await Task.Run(() => WindowFocusHelper.MinimizeAllWindows(processId));
            if (minimized > 0)
            {
                everMinimized = true;
                _logger.LogDebug("Restored minimized state for relaunched PID {Pid}", processId);
                return;
            }
        }

        if (!everMinimized)
            _logger.LogWarning("Gave up minimizing PID {Pid}: no visible window appeared within {Ms}ms", processId, totalMs);
    }

    private void OnHeartbeatReceived(HeartbeatReceivedEventArgs e)
    {
        var session = ActiveSessions.FirstOrDefault(s => s.Id == e.SessionId)
            ?? ActiveSessions.FirstOrDefault(s =>
                _sessionService.FindSessionByProcessId(s.ProcessId)?.Id == e.SessionId);
        if (session is null)
            return;

        ApplySessionSnapshot(session, e.Data.CharacterName, e.Data.Status, e.Data.Timestamp);
    }

    private void RefreshActiveTimeDisplay()
    {
        if (ActiveSessions.Count == 0)
            return;

        for (var i = 0; i < ActiveSessions.Count; i++)
        {
            var displayed = ActiveSessions[i];
            if (displayed.Status is GameSessionStatus.Offline or GameSessionStatus.Exiting)
                continue;

            var live = _sessionService.FindSessionByProcessId(displayed.ProcessId);
            if (live is null)
                continue;

            var aliveSeconds = live.GetAliveSeconds();
            if (displayed.Status == live.Status
                && displayed.CharacterName == live.CharacterName
                && displayed.UptimeSeconds == aliveSeconds)
            {
                continue;
            }

            var wasSelected = SelectedSession?.Id == displayed.Id;
            var updated = CloneSessionForUi(live);
            updated.UptimeSeconds = aliveSeconds;
            ActiveSessions[i] = updated;
            if (wasSelected)
                SelectedSession = updated;
        }
    }

    private void ApplySessionSnapshot(
        GameSession session,
        string characterName,
        GameSessionStatus status,
        DateTime lastHeartbeatUtc)
    {
        if (session.CharacterName == characterName && session.Status == status)
            return;

        var live = _sessionService.FindSessionByProcessId(session.ProcessId);
        var wasSelected = SelectedSession?.Id == session.Id;
        var idx = ActiveSessions.IndexOf(session);
        var updated = CloneSessionForUi(live ?? session);
        updated.CharacterName = characterName;
        updated.Status = status;
        updated.LastHeartbeatTime = lastHeartbeatUtc;
        updated.UptimeSeconds = updated.GetAliveSeconds();
        ActiveSessions[idx] = updated;
        if (wasSelected)
            SelectedSession = updated;

        if (updated.Status == GameSessionStatus.InGame
            && _pendingMinimizeOnInGame.Remove(updated.ProcessId))
        {
            _logger.LogInformation("Character '{Char}' reached in-game on PID {Pid} — minimizing now",
                updated.CharacterName, updated.ProcessId);
            _ = MinimizeWhenReadyAsync(updated.ProcessId);
        }
    }

    private static GameSession CloneSessionForUi(GameSession source) =>
        new()
        {
            Id = source.Id,
            AccountId = source.AccountId,
            AccountName = source.AccountName,
            ServerId = source.ServerId,
            ServerName = source.ServerName,
            CharacterName = source.CharacterName,
            ProcessId = source.ProcessId,
            Status = source.Status,
            StartedAtUtc = source.StartedAtUtc,
            WasMinimized = source.WasMinimized,
            LastHeartbeatTime = source.LastHeartbeatTime,
            UptimeSeconds = source.GetAliveSeconds()
        };

    private void FocusSelectedSession()
    {
        if (SelectedSession is null) return;

        if (!WindowFocusHelper.FocusProcess(SelectedSession.ProcessId))
            StatusText = $"Could not focus game window (PID {SelectedSession.ProcessId}). It may have closed.";
        else
            StatusText = $"Focused session PID {SelectedSession.ProcessId}";
    }
}
