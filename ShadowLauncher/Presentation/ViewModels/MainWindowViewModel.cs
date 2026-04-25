using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Application;
using ShadowLauncher.Infrastructure;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Infrastructure.Persistence;
using ShadowLauncher.Infrastructure.Updates;
using ShadowLauncher.Infrastructure.WebServices;
using ShadowLauncher.Services.Accounts;
using ShadowLauncher.Services.GameSessions;
using ShadowLauncher.Services.Launching;
using ShadowLauncher.Services.Monitoring;
using ShadowLauncher.Services.Dats;
using ShadowLauncher.Services.Servers;

namespace ShadowLauncher.Presentation.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly IAccountService _accountService;
    private readonly IServerService _serverService;
    private readonly IGameLauncher _gameLauncher;
    private readonly IGameSessionService _sessionService;
    private readonly IConfigurationProvider _config;
    private readonly UpdateChecker _updateChecker;
    private readonly ThemeService _themeService;
    private readonly IDatSetService _datSetService;
    private readonly AccountFileRepository _accountFileRepo;
    private readonly ServerFileRepository _serverFileRepo;
    private readonly ServerListDownloader _serverListDownloader;
    private readonly IGameMonitor _gameMonitor;
    private readonly ILogger<MainWindowViewModel> _logger;

    private string _statusText = "Ready";
    private bool _isLoading;
    private string _gameClientPath = string.Empty;
    private string _currentThemeName;

    /// <summary>Tracks PID → (Account, Server) for auto-relaunch.</summary>
    private readonly Dictionary<int, (Account Account, Server Server)> _launchedSessions = [];

    /// <summary>
    /// Raised when the VM needs the view to show a file-browse dialog.
    /// </summary>
    public event EventHandler? BrowseGameClientRequested;

    public MainWindowViewModel(
        IAccountService accountService,
        IServerService serverService,
        IGameLauncher gameLauncher,
        IGameSessionService sessionService,
        IConfigurationProvider config,
        AccountFileRepository accountFileRepo,
        ServerFileRepository serverFileRepo,
        ServerListDownloader serverListDownloader,
        AppCoordinator appCoordinator,
        IGameMonitor gameMonitor,
        UpdateChecker updateChecker,
        ThemeService themeService,
        IDatSetService datSetService,
        ILogger<MainWindowViewModel> logger)
    {
        _accountService = accountService;
        _serverService = serverService;
        _gameLauncher = gameLauncher;
        _sessionService = sessionService;
        _config = config;
        _updateChecker = updateChecker;
        _themeService = themeService;
        _datSetService = datSetService;
        _accountFileRepo = accountFileRepo;
        _serverFileRepo = serverFileRepo;
        _serverListDownloader = serverListDownloader;
        _gameMonitor = gameMonitor;
        _logger = logger;

        _currentThemeName = _themeService.CurrentThemeName;
        _themeService.ThemeChanged += name =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => CurrentThemeName = name);

        _logger.LogInformation("MainWindowViewModel initializing");

        _accountFileRepo.AccountsChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(ReloadAccountsAsync);
        _serverFileRepo.ServersChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(ReloadServersAsync);
        appCoordinator.ServerStatusRefreshed += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(ReloadServersAsync);
        _gameMonitor.GameExited += (_, e) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnGameExited(e.ProcessId));
        _gameMonitor.HeartbeatReceived += (_, e) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnHeartbeatReceived(e));

        _gameClientPath = _config.GameClientPath;

        SelectedAccounts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanLaunch));
        SelectedServers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanLaunch));

        LaunchCommand = new AsyncRelayCommand(LaunchGameAsync, () => CanLaunch);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        AddAccountCommand = new AsyncRelayCommand(AddAccountAsync);
        AddServerCommand = new AsyncRelayCommand(AddServerAsync);
        BrowseServersCommand = new RelayCommand(BrowseServers);
        RemoveServersCommand = new AsyncRelayCommand(RemoveSelectedServersAsync, () => SelectedServers.Count > 0);
        SettingsCommand = new RelayCommand(OpenSettings);
        BrowseGameClientCommand = new RelayCommand(() => BrowseGameClientRequested?.Invoke(this, EventArgs.Empty));
        FocusSessionCommand = new RelayCommand(FocusSelectedSession, () => SelectedSession is not null);
        OpenAccountsFileCommand = new RelayCommand(OpenAccountsFile);
        MinimizeSessionCommand = new RelayCommand(p => MinimizeSession(p as GameSession));
        RestoreSessionCommand  = new RelayCommand(p => RestoreSession(p as GameSession));
        MinimizeAllSessionsCommand = new RelayCommand(MinimizeAllSessions);
        RestoreAllSessionsCommand  = new RelayCommand(RestoreAllSessions);
        PreviousThemeCommand = new RelayCommand(PreviousTheme);
        NextThemeCommand = new RelayCommand(NextTheme);
    }

    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<Server> Servers { get; } = [];
    public ObservableCollection<GameSession> ActiveSessions { get; } = [];

    private GameSession? _selectedSession;
    public GameSession? SelectedSession
    {
        get => _selectedSession;
        set => SetProperty(ref _selectedSession, value);
    }

    public ObservableCollection<Account> SelectedAccounts { get; } = [];
    public ObservableCollection<Server> SelectedServers { get; } = [];

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string GameClientPath
    {
        get => _gameClientPath;
        set
        {
            if (SetProperty(ref _gameClientPath, value))
            {
                _config.GameClientPath = value;
                _config.Save();
                OnPropertyChanged(nameof(CanLaunch));
            }
        }
    }

    public bool NeverKillOnMissingHeartbeat
    {
        get => !_config.KillOnMissingHeartbeat;
        set
        {
            _config.KillOnMissingHeartbeat = !value;
            _config.Save();
            OnPropertyChanged();
        }
    }

    public bool KillOnMissingHeartbeat
    {
        get => _config.KillOnMissingHeartbeat;
        set
        {
            if (_config.KillOnMissingHeartbeat != value)
            {
                _config.KillOnMissingHeartbeat = value;
                _config.Save();
                OnPropertyChanged();
            }
        }
    }

    public int KillHeartbeatTimeoutSeconds
    {
        get => _config.KillHeartbeatTimeoutSeconds;
        set
        {
            if (value >= 5 && _config.KillHeartbeatTimeoutSeconds != value)
            {
                _config.KillHeartbeatTimeoutSeconds = value;
                _config.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool AutoRelaunch
    {
        get => _config.AutoRelaunch;
        set
        {
            if (_config.AutoRelaunch != value)
            {
                _config.AutoRelaunch = value;
                _config.Save();
                OnPropertyChanged();
            }
        }
    }

    public int AutoRelaunchDelaySeconds
    {
        get => _config.AutoRelaunchDelaySeconds;
        set
        {
            if (value > 0 && _config.AutoRelaunchDelaySeconds != value)
            {
                _config.AutoRelaunchDelaySeconds = value;
                _config.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool CanLaunch => SelectedAccounts.Count > 0 && SelectedServers.Count > 0
        && !IsLoading && File.Exists(GameClientPath);

    public string CurrentVersion { get; } =
        $"v{UpdateChecker.CurrentVersion.Major}.{UpdateChecker.CurrentVersion.Minor}.{UpdateChecker.CurrentVersion.Build}";

    public ICommand LaunchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddAccountCommand { get; }
    public ICommand AddServerCommand { get; }
    public ICommand BrowseServersCommand { get; }
    public ICommand RemoveServersCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand BrowseGameClientCommand { get; }
    public ICommand FocusSessionCommand { get; }
    public ICommand OpenAccountsFileCommand { get; }
    public ICommand MinimizeSessionCommand { get; }
    public ICommand RestoreSessionCommand { get; }
    public ICommand MinimizeAllSessionsCommand { get; }
    public ICommand RestoreAllSessionsCommand { get; }
    public ICommand PreviousThemeCommand { get; }
    public ICommand NextThemeCommand { get; }

    public string CurrentThemeName
    {
        get => _currentThemeName;
        private set => SetProperty(ref _currentThemeName, value);
    }

    private void PreviousTheme()
    {
        _themeService.Previous();
        _config.Theme = _themeService.CurrentThemeName;
        _config.Save();
    }

    private void NextTheme()
    {
        _themeService.Next();
        _config.Theme = _themeService.CurrentThemeName;
        _config.Save();
    }

    private void OpenAccountsFile()
    {
        var path = Path.Combine(_config.DataDirectory, "accounts.txt");
        if (File.Exists(path))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
        else
            StatusText = "Accounts file not found.";
    }

    public async Task LoadAsync()
    {
        _logger.LogInformation("Loading accounts, servers, and sessions...");

        IsLoading = true;
        StatusText = "Loading...";
        try
        {
            Accounts.Clear();
            foreach (var account in await _accountService.GetAllAccountsAsync())
                Accounts.Add(account);

            Servers.Clear();
            foreach (var server in await _serverService.GetAllServersAsync())
                Servers.Add(server);

            ActiveSessions.Clear();
            foreach (var session in await _sessionService.GetActiveSessionsAsync())
                ActiveSessions.Add(session);

            StatusText = $"Loaded {Accounts.Count} accounts, {Servers.Count} servers";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ReloadAccountsAsync()
    {
        _logger.LogInformation("Reloading accounts from file...");
        Accounts.Clear();
        foreach (var account in await _accountService.GetAllAccountsAsync())
            Accounts.Add(account);
        StatusText = $"Accounts reloaded ({Accounts.Count} accounts)";
    }

    /// <summary>Raised after a server reload so the view can restore ListBox selection by ID.</summary>
    public event Action<IReadOnlyList<string>>? ServerSelectionRestoreRequested;

    private async Task ReloadServersAsync()
    {
        _logger.LogDebug("Reloading servers from file...");
        var selectedIds = SelectedServers.Select(s => s.Id).ToList();
        Servers.Clear();
        foreach (var server in await _serverService.GetAllServersAsync())
            Servers.Add(server);
        if (selectedIds.Count > 0)
            ServerSelectionRestoreRequested?.Invoke(selectedIds);
    }

    private async void OnGameExited(int processId)
    {
        _logger.LogInformation("Game process exited: PID {Pid}", processId);
        var session = ActiveSessions.FirstOrDefault(s => s.ProcessId == processId);
        if (session is not null)
        {
            ActiveSessions.Remove(session);
            StatusText = $"Session ended: {session.AccountName} on {session.ServerName}";
        }

        // Auto-relaunch if enabled and we know which account/server this was
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
                var character = info.Account.Characters.FirstOrDefault()
                    ?? new Core.Models.Character { Id = Guid.NewGuid().ToString(), Name = "Default", AccountId = info.Account.Id, Level = 1 };

                // Delay before relaunch
                await Task.Delay(AutoRelaunchDelaySeconds * 1000);

                var result = await _gameLauncher.LaunchGameAsync(info.Account, character, info.Server);
                if (result.Success)
                {
                    var newSession = await _sessionService.CreateSessionAsync(info.Account, info.Server, result.ProcessId);
                    ActiveSessions.Add(newSession);
                    _launchedSessions[result.ProcessId] = info;
                    StatusText = $"Auto-relaunched {info.Account.Name} (PID {result.ProcessId})";
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

    private void OnHeartbeatReceived(HeartbeatReceivedEventArgs e)
    {
        var session = ActiveSessions.FirstOrDefault(s => s.Id == e.SessionId);
        if (session is not null)
        {
            var wasSelected = SelectedSession?.Id == session.Id;
            var idx = ActiveSessions.IndexOf(session);
            // Create a new object so ObservableCollection detects the change
            var updated = new Core.Models.GameSession
            {
                Id = session.Id,
                AccountId = session.AccountId,
                AccountName = session.AccountName,
                ServerId = session.ServerId,
                ServerName = session.ServerName,
                CharacterName = e.Data.CharacterName,
                ProcessId = session.ProcessId,
                ServerMonitorPort = session.ServerMonitorPort,
                Status = e.Data.Status,
                StartTime = session.StartTime,
                LastHeartbeatTime = e.Data.Timestamp,
                UptimeSeconds = e.Data.UptimeSeconds
            };
            ActiveSessions[idx] = updated;
            if (wasSelected)
                SelectedSession = updated;
        }
    }

    private async Task LaunchGameAsync()
    {
        var accounts = SelectedAccounts.ToList();
        var servers = SelectedServers.ToList();
        if (accounts.Count == 0 || servers.Count == 0) return;

        _logger.LogInformation("Launch requested: {AccountCount} accounts × {ServerCount} servers",
            accounts.Count, servers.Count);

        var serversNeedingDats = new List<(Server Server, string DatSetId)>();
        foreach (var server in servers.DistinctBy(s => s.DatSetId))
        {
            if (string.IsNullOrWhiteSpace(server.DatSetId)) continue;
            if (!await _datSetService.IsDatSetReadyAsync(server.DatSetId))
                serversNeedingDats.Add((server, server.DatSetId));
        }

        // Collect custom-source servers (local path or zip URL) that still need their
        // DATs fetched. Local paths are validated; zip URLs are downloaded if not cached.
        var serversNeedingCustomDats = servers
            .DistinctBy(s => s.Id)
            .Where(s => !string.IsNullOrWhiteSpace(s.CustomDatRegistryPath)
                     || !string.IsNullOrWhiteSpace(s.CustomDatZipUrl))
            .ToList();

        if (serversNeedingCustomDats.Count > 0 || serversNeedingDats.Count > 0)
        {
            var fetchWindow = new Presentation.Views.DatFetchWindow(
                System.Windows.Application.Current.MainWindow);
            fetchWindow.Show();

            try
            {
                // Registry-sourced DAT sets
                foreach (var (fetchServer, datSetId) in serversNeedingDats)
                {
                    _logger.LogInformation("Fetching DAT set '{Id}' for server '{Server}'",
                        datSetId, fetchServer.Name);

                    var progress = new Progress<Services.Dats.DatDownloadProgress>(
                        p => fetchWindow.ViewModel.Apply(p));

                    await _datSetService.DownloadMissingFilesAsync(datSetId, progress);
                }

                // Custom-source DAT servers (local path or hosted zip URL)
                foreach (var fetchServer in serversNeedingCustomDats)
                {
                    _logger.LogInformation("Ensuring custom DAT source for server '{Server}'", fetchServer.Name);

                    var progress = new Progress<Services.Dats.DatDownloadProgress>(
                        p => fetchWindow.ViewModel.Apply(p));

                    await _datSetService.EnsureCustomDatSourceReadyAsync(fetchServer, progress);
                }

                fetchWindow.ViewModel.SetComplete();
                await Task.Delay(600); // brief "Done" display
            }
            catch (Exception ex)
            {
                fetchWindow.Close();
                _logger.LogError(ex, "DAT prefetch failed");
                StatusText = $"DAT download failed: {ex.Message}";
                MessageBox.Show(
                    $"Failed to fetch DAT files:\n\n{ex.Message}",
                    "DAT Download Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            finally
            {
                fetchWindow.Close();
            }
        }

        int launched = 0, failed = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var account in accounts)
        {
            foreach (var server in servers)
            {
                // Skip if already tracked by session service (heartbeat) OR by our PID map.
                var alreadyActive =
                    ActiveSessions.Any(s =>
                        string.Equals(s.AccountId, account.Id, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(s.ServerId, server.Id, StringComparison.OrdinalIgnoreCase))
                    || _launchedSessions.Values.Any(v =>
                        string.Equals(v.Account.Id, account.Id, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(v.Server.Id, server.Id, StringComparison.OrdinalIgnoreCase));

                if (alreadyActive)
                {
                    skipped++;
                    continue;
                }

                var character = account.Characters.FirstOrDefault()
                    ?? new Core.Models.Character { Id = Guid.NewGuid().ToString(), Name = "Default", AccountId = account.Id, Level = 1 };

                StatusText = $"Launching {account.Name} on {server.Name}...";
                try
                {
                    var result = await _gameLauncher.LaunchGameAsync(account, character, server);
                    if (result.Success)
                    {
                        var session = await _sessionService.CreateSessionAsync(account, server, result.ProcessId);
                        ActiveSessions.Add(session);
                        _launchedSessions[result.ProcessId] = (account, server);
                        account.LaunchCount++;
                        account.LastUsedDate = DateTime.UtcNow;
                        await _accountService.UpdateAccountAsync(account);
                        launched++;
                    }
                    else
                    {
                        var msg = result.ErrorMessage ?? "Unknown error";
                        _logger.LogError("Launch failed for {Account} on {Server}: {Error}",
                            account.Name, server.Name, msg);
                        errors.Add($"{account.Name} on {server.Name}: {msg}");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Launch exception for {Account} on {Server}",
                        account.Name, server.Name);
                    errors.Add($"{account.Name} on {server.Name}: {ex.Message}");
                    failed++;
                }
            }
        }

        if (failed == 0)
        {
            StatusText = skipped == 0
                ? $"Launched {launched} session(s)"
                : $"Launched {launched}, skipped {skipped} already active";
        }
        else
        {
            StatusText = $"Launched {launched}, failed {failed}" + (skipped > 0 ? $", skipped {skipped}" : "");
            MessageBox.Show(
                string.Join("\n\n", errors),
                "Launch Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task RefreshAsync()
    {
        StatusText = "Refreshing...";
        await _serverService.RefreshAllServerStatusAsync();
        await ReloadAccountsAsync();
        await ReloadServersAsync();
        StatusText = $"Refreshed {Accounts.Count} accounts, {Servers.Count} servers";
    }

    private async Task AddAccountAsync()
    {
        var vm = new AddAccountViewModel();
        var window = new Presentation.Views.AddAccountWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            try
            {
                var account = await _accountService.CreateAccountAsync(vm.Username, vm.Password);
                Accounts.Add(account);
                StatusText = $"Account '{account.Name}' added.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error adding account: {ex.Message}";
            }
        }
    }

    private void FocusSelectedSession()
    {
        if (SelectedSession is null) return;

        if (!WindowFocusHelper.FocusProcess(SelectedSession.ProcessId))
            StatusText = $"Could not focus game window (PID {SelectedSession.ProcessId}). It may have closed.";
        else
            StatusText = $"Focused session PID {SelectedSession.ProcessId}";
    }

    private void MinimizeSession(GameSession? session)
    {
        if (session is null) return;
        WindowFocusHelper.MinimizeProcess(session.ProcessId);
    }

    private void RestoreSession(GameSession? session)
    {
        if (session is null) return;
        WindowFocusHelper.RestoreProcess(session.ProcessId);
    }

    private void MinimizeAllSessions()
    {
        foreach (var session in ActiveSessions)
            WindowFocusHelper.MinimizeProcess(session.ProcessId);
    }

    private void RestoreAllSessions()
    {
        foreach (var session in ActiveSessions)
            WindowFocusHelper.RestoreProcess(session.ProcessId);
    }

    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_config, _updateChecker, _themeService);
        var window = new Presentation.Views.SettingsWindow(vm, _accountService, _serverService);
        window.Owner = System.Windows.Application.Current.MainWindow;
        if (window.ShowDialog() == true)
        {
            StatusText = "Settings saved.";
        }
    }

    private async Task AddServerAsync()
    {
        var vm = new AddServerViewModel(_config);
        var window = new Presentation.Views.AddServerWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            try
            {
                var server = await _serverService.CreateServerAsync(vm.ToServer());
                Servers.Add(server);
                StatusText = $"Server '{server.Name}' added.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error adding server: {ex.Message}";
            }
        }
    }

    private async Task RemoveSelectedServersAsync()
    {
        var toRemove = SelectedServers.ToList();
        foreach (var server in toRemove)
        {
            await _serverService.DeleteServerAsync(server.Id);
            Servers.Remove(server);

            // Clean up the DAT set cache if no remaining server uses it.
            if (!string.IsNullOrWhiteSpace(server.DatSetId))
            {
                var stillNeeded = Servers.Any(s =>
                    string.Equals(s.DatSetId, server.DatSetId, StringComparison.OrdinalIgnoreCase));
                if (!stillNeeded)
                {
                    var cacheDir = _datSetService.GetLocalDatSetPath(server.DatSetId);
                    if (Directory.Exists(cacheDir))
                    {
                        try
                        {
                            Directory.Delete(cacheDir, recursive: true);
                            _logger.LogInformation("Removed DAT set cache for '{DatSetId}'", server.DatSetId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not remove DAT set cache for '{DatSetId}'", server.DatSetId);
                        }
                    }
                }
            }
        }
        SelectedServers.Clear();
        StatusText = $"Removed {toRemove.Count} server(s)";
    }

    public async Task RemoveServerAsync(Server server)
    {
        await _serverService.DeleteServerAsync(server.Id);
        Servers.Remove(server);
        SelectedServers.Remove(server);
        StatusText = $"Removed server '{server.Name}'";
    }

    public async Task UpdateServerAsync(Server server)
    {
        await _serverService.UpdateServerAsync(server);
        await ReloadServersAsync();
        StatusText = $"Server '{server.Name}' updated";
    }

    public async Task RemoveAccountAsync(Account account)
    {
        await _accountService.DeleteAccountAsync(account.Id);
        Accounts.Remove(account);
        SelectedAccounts.Remove(account);
        StatusText = $"Removed account '{account.Name}'";
    }

    public async Task UpdateAccountNoteAsync(Account account)
    {
        await _accountService.UpdateAccountAsync(account);
        await ReloadAccountsAsync();
    }

    private void BrowseServers()
    {
        var vm = new BrowseServersViewModel(_serverListDownloader);
        vm.ServerAdded += async (_, server) =>
        {
            try
            {
                // Create a copy so the browse list object isn't mutated
                var copy = new Core.Models.Server
                {
                    Name = server.Name,
                    Emulator = server.Emulator,
                    Description = server.Description,
                    Hostname = server.Hostname,
                    Port = server.Port,
                    DiscordUrl = server.DiscordUrl,
                    WebsiteUrl = server.WebsiteUrl,
                    DefaultRodat = server.DefaultRodat,
                    SecureLogon = server.SecureLogon,
                    DatSetId = server.DatSetId
                        ?? await _datSetService.ResolveDatSetIdForServerAsync(server.Name)
                };

                var added = await _serverService.CreateServerAsync(copy);
                // Check status immediately
                await _serverService.CheckServerStatusAsync(added.Id);
                var refreshed = await _serverService.GetServerAsync(added.Id);
                Servers.Add(refreshed ?? added);
                vm.StatusText = $"Added '{added.Name}'";
            }
            catch (InvalidOperationException)
            {
                vm.StatusText = $"'{server.Name}' is already in your server list.";
            }
            catch (Exception ex)
            {
                vm.StatusText = $"Error: {ex.Message}";
            }
        };

        var window = new Presentation.Views.BrowseServersWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }
}
