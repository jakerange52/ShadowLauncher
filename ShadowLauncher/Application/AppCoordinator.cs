using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Services.Dats;
using ShadowLauncher.Services.Monitoring;
using ShadowLauncher.Services.Servers;

namespace ShadowLauncher.Application;

public class AppCoordinator
{
    private readonly IConfigurationProvider _config;
    private readonly IGameMonitor _gameMonitor;
    private readonly IServerService _serverService;
    private readonly IEventAggregator _events;
    private readonly IDatSetService _datSetService;
    private readonly FirstRunService _firstRunService;
    private readonly ILogger<AppCoordinator> _logger;
    private CancellationTokenSource? _appCts;
    private Task? _serverMonitorTask;

    public event EventHandler? ServerStatusRefreshed;

    public AppCoordinator(
        IConfigurationProvider config,
        IGameMonitor gameMonitor,
        IServerService serverService,
        IEventAggregator events,
        IDatSetService datSetService,
        FirstRunService firstRunService,
        ILogger<AppCoordinator> logger)
    {
        _config = config;
        _gameMonitor = gameMonitor;
        _serverService = serverService;
        _events = events;
        _datSetService = datSetService;
        _firstRunService = firstRunService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("ShadowLauncher initializing...");
        _appCts = new CancellationTokenSource();

        // Ensure data directories exist
        Directory.CreateDirectory(_config.DataDirectory);
        Directory.CreateDirectory(_config.LogDirectory);

        // Silently detect AC client and import ThwargLauncher data on first launch.
        await _firstRunService.RunAsync();

        // Fetch a fresh DatRegistry.xml in the background so checksums and server
        // mappings are always up to date. Failures are non-fatal — the bundled or
        // cached copy will be used instead.
        _ = Task.Run(async () =>
        {
            try
            {
                await _datSetService.RefreshRegistryAsync();
                _logger.LogInformation("DatRegistry refreshed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DatRegistry refresh failed, using cached copy");
            }
        });

        // Start background monitoring
        await _gameMonitor.StartMonitoringAsync(_appCts.Token);

        // Start periodic server status checks
        _serverMonitorTask = ServerStatusLoopAsync(_appCts.Token);

        _logger.LogInformation("ShadowLauncher initialized successfully");
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

    public async Task ShutdownAsync()
    {
        _logger.LogInformation("ShadowLauncher shutting down...");

        if (_appCts is not null)
        {
            await _appCts.CancelAsync();
            if (_serverMonitorTask is not null)
                try { await _serverMonitorTask; } catch (OperationCanceledException) { }
            _appCts.Dispose();
        }

        await _gameMonitor.StopMonitoringAsync();
        _config.Save();

        _logger.LogInformation("ShadowLauncher shutdown complete");
    }
}
