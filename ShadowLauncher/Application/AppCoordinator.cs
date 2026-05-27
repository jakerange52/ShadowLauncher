using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.FileSystem;
// using ShadowLauncher.Infrastructure.Native; // symlink — re-enable with SymlinkLauncher
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Services.Dats;
using ShadowLauncher.Services.GameSessions;
using ShadowLauncher.Services.Launching;
using ShadowLauncher.Services.Monitoring;
using ShadowLauncher.Services.Servers;

namespace ShadowLauncher.Application;

public class AppCoordinator
{
    private readonly IConfigurationProvider _config;
    private readonly IGameMonitor _gameMonitor;
    private readonly IServerService _serverService;
    private readonly IDatSetService _datSetService;
    private readonly FirstRunService _firstRunService;
    private readonly IInstancePreparer _instancePreparer;
    private readonly IGameSessionService _sessionService;
    private readonly SessionJournal _sessionJournal;
    private readonly IGameLauncher _gameLauncher;
    private readonly ILogger<AppCoordinator> _logger;
    private CancellationTokenSource? _appCts;
    private Task? _serverMonitorTask;
    private Task? _datRefreshTask;
    private Task? _instanceCleanupTask;

    public event EventHandler? ServerStatusRefreshed;
    // public event EventHandler<SymlinkPrivilegeHelper.PrivilegeStatus>? SymlinkPrivilegeChecked; // symlink
    /// <summary>
    /// Raised when the ACBase directory needs to be populated before HardLinkLauncher can run.
    /// Subscribers should show a progress window and await the copy task via the event args.
    /// </summary>
    public event EventHandler<Func<IProgress<AcBaseCopyProgress>, Task>>? AcBaseCopyRequired;

    public AppCoordinator(
        IConfigurationProvider config,
        IGameMonitor gameMonitor,
        IServerService serverService,
        IDatSetService datSetService,
        FirstRunService firstRunService,
        IInstancePreparer instancePreparer,
        IGameSessionService sessionService,
        SessionJournal sessionJournal,
        IGameLauncher gameLauncher,
        ILogger<AppCoordinator> logger)
    {
        _config = config;
        _gameMonitor = gameMonitor;
        _serverService = serverService;
        _datSetService = datSetService;
        _firstRunService = firstRunService;
        _instancePreparer = instancePreparer;
        _sessionService = sessionService;
        _sessionJournal = sessionJournal;
        _gameLauncher = gameLauncher;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("ShadowLauncher initializing...");
        _appCts = new CancellationTokenSource();

        // Ensure data directories exist
        Directory.CreateDirectory(_config.DataDirectory);
        Directory.CreateDirectory(_config.LogDirectory);

        // Delete any leftover update installer from a previous update run.
        // This retroactively cleans up %TEMP%\ShadowLauncher-Setup-Update.exe
        // for users who updated before the cmd command was patched to do so.
        CleanupStaleTempInstaller();

        // Remove instance directories left over from a previous session.
        // Also sweeps the Instances\ root when it becomes empty (fix #5).
        _instancePreparer.CleanupStaleInstances();
        var instancesRoot = Path.Combine(_config.DataDirectory, "Instances");
        if (Directory.Exists(instancesRoot) && !Directory.EnumerateFileSystemEntries(instancesRoot).Any())
        {
            try { Directory.Delete(instancesRoot); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not remove empty Instances directory"); }
        }

        // Silently detect AC client and import ThwargLauncher data on first launch.
        await _firstRunService.RunAsync();

        // If HardLinkLauncher is active, ensure ACBase is ready before any launch is attempted.
        if (_instancePreparer is HardLinkLauncher)
        {
            if (AcBaseCopyRequired is not null && _firstRunService.HardLinkBaseNeedsCopy())
            {
                var tcs = new TaskCompletionSource();
                Func<IProgress<AcBaseCopyProgress>, Task> copyTask = async progress =>
                {
                    await _firstRunService.PrepareHardLinkBaseAsync(progress);
                    tcs.SetResult();
                };
                AcBaseCopyRequired.Invoke(this, copyTask);
                await tcs.Task;
            }
            else
            {
                await _firstRunService.PrepareHardLinkBaseAsync();
                }
        }

        // Ensure SeCreateSymbolicLinkPrivilege is active — covers users who installed
        // an older build before the installer granted it unconditionally.
        // var privilegeStatus = SymlinkPrivilegeHelper.EnsurePrivilege(_logger); // symlink
        // if (privilegeStatus != SymlinkPrivilegeHelper.PrivilegeStatus.AlreadyActive) // symlink
        //     SymlinkPrivilegeChecked?.Invoke(this, privilegeStatus); // symlink

        // Fetch a fresh DatRegistry.xml in the background so checksums and server
        // mappings are always up to date. Failures are non-fatal — the bundled or
        // cached copy will be used instead.
        _datRefreshTask = Task.Run(async () =>
        {
            try
            {
                await _datSetService.RefreshRegistryAsync(_appCts.Token);
                _logger.LogInformation("DatRegistry refreshed successfully");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DatRegistry refresh failed, using cached copy");
            }
        }, _appCts.Token);

        // Start background monitoring
        await _gameMonitor.StartMonitoringAsync(_appCts.Token);

        // Re-adopt any game sessions that survived a launcher restart, and clean up
        // the remnants of sessions whose processes are no longer running.
        await ReconcileJournaledSessionsAsync();

        // Start periodic server status checks
        _serverMonitorTask = ServerStatusLoopAsync(_appCts.Token);

        // Periodically sweep the Instances directory for stale dirs that couldn't be
        // deleted immediately (e.g. shared inodes locked by other running clients).
        _instanceCleanupTask = InstanceCleanupLoopAsync(_appCts.Token);

        _logger.LogInformation("ShadowLauncher initialized successfully");
    }

    private void CleanupStaleTempInstaller()
    {
        var tempInstaller = Path.Combine(Path.GetTempPath(), "ShadowLauncher-Setup-Update.exe");
        if (!File.Exists(tempInstaller)) return;
        try
        {
            File.Delete(tempInstaller);
            _logger.LogInformation("Cleaned up stale temp installer: {Path}", tempInstaller);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete stale temp installer at {Path}", tempInstaller);
        }
    }

    /// <summary>
    /// Reads the on-disk session journal written by a previous launcher run and for
    /// each entry either re-adopts the session (if the game process is still alive) or
    /// performs cleanup (ThwargFilter launch file + journal entry) if the process is gone.
    /// This is what keeps tracking correct across launcher restarts and handles the case
    /// where the game outlives the launcher.
    /// </summary>
    private async Task ReconcileJournaledSessionsAsync()
    {
        var journaled = _sessionJournal.ReadAll();
        if (journaled.Count == 0) return;

        _logger.LogInformation("Session journal: reconciling {Count} persisted session(s)", journaled.Count);

        foreach (var session in journaled)
        {
            var alive = await _gameLauncher.IsGameProcessRunningAsync(session.ProcessId);
            if (alive)
            {
                // Process is still running — restore the session so the monitor loop
                // picks it up and starts tracking heartbeat / exit as normal.
                session.Status = GameSessionStatus.InGame;
                session.LastHeartbeatTime = DateTime.UtcNow;
                await _sessionService.RestoreSessionAsync(session);
                _logger.LogInformation(
                    "Restored live session {Id} — PID {Pid} ({Account} on {Server})",
                    session.Id, session.ProcessId, session.AccountName, session.ServerName);
            }
            else
            {
                // Process is gone — clean up any remnants and discard the journal entry.
                _gameLauncher.CleanupThwargFilterLaunchFile(session.AccountName, session.ServerName);
                _sessionJournal.Delete(session.Id);
                _logger.LogInformation(
                    "Dead session {Id} cleaned up — PID {Pid} was no longer running",
                    session.Id, session.ProcessId);
            }
        }
    }

    private async Task ServerStatusLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _serverService.RefreshAllServerStatusAsync();
                ServerStatusRefreshed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Server status check failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task InstanceCleanupLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(15), token); }
            catch (OperationCanceledException) { break; }

            try { _instancePreparer.CleanupStaleInstances(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Periodic instance cleanup failed"); }
        }
    }

    public async Task ShutdownAsync()
    {
        _logger.LogInformation("ShadowLauncher shutting down...");

        if (_appCts is not null)
        {
            await _appCts.CancelAsync();
            if (_serverMonitorTask is not null)
                try { await _serverMonitorTask; } catch (OperationCanceledException) { }
            if (_datRefreshTask is not null)
                try { await _datRefreshTask; } catch (OperationCanceledException) { }
            if (_instanceCleanupTask is not null)
                try { await _instanceCleanupTask; } catch (OperationCanceledException) { }
            _appCts.Dispose();
        }

        await _gameMonitor.StopMonitoringAsync();
        _config.Save();

        _logger.LogInformation("ShadowLauncher shutdown complete");
    }
}
