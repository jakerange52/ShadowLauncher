using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Infrastructure.Persistence;

namespace ShadowLauncher.Services.Monitoring;

/// <summary>
/// Per-account game window placement save/restore.
/// Restore is deferred after InGame so SetWindowPlacement does not race
/// UtilityBelt / Decal view init against the DirectX swap chain.
/// </summary>
public sealed class GameWindowPlacementService
{
    /// <summary>
    /// Wait this long after first confirmed InGame before SetWindowPlacement.
    /// Matches the remize settle delay — UB HUDs often bind in the first few seconds in-world.
    /// </summary>
    private static readonly TimeSpan DxSettleAfterInGame = TimeSpan.FromSeconds(8);

    private readonly IConfigurationProvider _config;
    private readonly GameWindowPlacementStore _store;
    private readonly ILogger<GameWindowPlacementService> _logger;

    private readonly Dictionary<int, bool> _hasRestored = [];
    private readonly Dictionary<int, string> _lastSavedPlacement = [];
    private readonly Dictionary<int, DateTime> _inGameSinceUtc = [];

    public GameWindowPlacementService(
        IConfigurationProvider config,
        GameWindowPlacementStore store,
        ILogger<GameWindowPlacementService> logger)
    {
        _config = config;
        _store = store;
        _logger = logger;
    }

    public void ClearSession(int processId)
    {
        _hasRestored.Remove(processId);
        _lastSavedPlacement.Remove(processId);
        _inGameSinceUtc.Remove(processId);
    }

    public void ProcessSession(GameSession session, GameSessionStatus status)
    {
        if (status != GameSessionStatus.InGame)
        {
            _inGameSinceUtc.Remove(session.ProcessId);
            return;
        }

        if (string.IsNullOrWhiteSpace(session.ServerName) || string.IsNullOrWhiteSpace(session.AccountName))
            return;

        var hwnd = WindowFocusHelper.FindWindowForProcess(session.ProcessId);
        if (hwnd == nint.Zero)
            return;

        if (!_inGameSinceUtc.ContainsKey(session.ProcessId))
            _inGameSinceUtc[session.ProcessId] = DateTime.UtcNow;

        var settled = DateTime.UtcNow - _inGameSinceUtc[session.ProcessId] >= DxSettleAfterInGame;

        if (_config.RestoreGameWindows && !_hasRestored.GetValueOrDefault(session.ProcessId))
        {
            if (!settled)
                return;

            TryRestore(session, hwnd);
            _hasRestored[session.ProcessId] = true;
        }

        // Avoid saving pre-restore geometry; once settled (or restore disabled), persist.
        if (_config.SaveGameWindows && (settled || !_config.RestoreGameWindows || _hasRestored.GetValueOrDefault(session.ProcessId)))
            TrySave(session, hwnd);
    }

    public void SaveCurrentPlacement(GameSession session)
    {
        if (string.IsNullOrWhiteSpace(session.ServerName) || string.IsNullOrWhiteSpace(session.AccountName))
            return;

        var hwnd = WindowFocusHelper.FindWindowForProcess(session.ProcessId);
        if (hwnd == nint.Zero)
            return;

        TrySave(session, hwnd, force: true);
    }

    public void DisableWindowPosition()
    {
        _config.SaveGameWindows = false;
        _config.RestoreGameWindows = false;
        _config.Save();
        _logger.LogInformation("Game window position management disabled");
    }

    public void LockWindowPosition(GameSession session)
    {
        SaveCurrentPlacement(session);
        _config.SaveGameWindows = false;
        _config.RestoreGameWindows = true;
        _config.Save();
        _logger.LogInformation("Game window positions locked for {Account}@{Server}", session.AccountName, session.ServerName);
    }

    public void UnlockWindowPosition()
    {
        _config.SaveGameWindows = true;
        _config.RestoreGameWindows = true;
        _config.Save();
        _logger.LogInformation("Game window position management unlocked");
    }

    private void TryRestore(GameSession session, nint hwnd)
    {
        var placementString = _store.GetPlacement(session.ServerName, session.AccountName);
        if (GameWindowPlacementHelper.IsEmpty(placementString))
            return;

        if (GameWindowPlacementHelper.TrySetPlacement(hwnd, placementString!))
        {
            _logger.LogDebug(
                "Restored game window placement for {Account}@{Server} (PID {Pid})",
                session.AccountName, session.ServerName, session.ProcessId);
        }
    }

    private void TrySave(GameSession session, nint hwnd, bool force = false)
    {
        var placementString = GameWindowPlacementHelper.GetPlacementString(hwnd);
        if (GameWindowPlacementHelper.IsEmpty(placementString))
            return;

        if (!force && _lastSavedPlacement.TryGetValue(session.ProcessId, out var last) && last == placementString)
            return;

        _store.SetPlacement(session.ServerName, session.AccountName, placementString!);
        _lastSavedPlacement[session.ProcessId] = placementString!;
    }
}
