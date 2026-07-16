using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Application;
using ShadowLauncher.Infrastructure;
using ShadowLauncher.Infrastructure.Updates;
using ShadowLauncher.Infrastructure.WebServices;
using ShadowLauncher.Services.Accounts;
using ShadowLauncher.Services.GameSessions;
using ShadowLauncher.Services.Launching;
using ShadowLauncher.Services.Monitoring;
using ShadowLauncher.Services.Dats;
using ShadowLauncher.Services.Profiles;
using ShadowLauncher.Services.LoginCommands;
using ShadowLauncher.Services.Servers;

namespace ShadowLauncher.Presentation.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IAccountService _accountService;
    private readonly IServerService _serverService;
    private readonly IGameLauncher _gameLauncher;
    private readonly IGameSessionService _sessionService;
    private readonly IConfigurationProvider _config;
    private readonly UpdateChecker _updateChecker;
    private readonly ThemeService _themeService;
    private readonly IDatSetService _datSetService;
    private readonly ServerListDownloader _serverListDownloader;
    private readonly BetaServerListDownloader _betaServerListDownloader;
    private readonly TreeStatsService _treeStatsService;
    private readonly ProfileService _profileService;
    private readonly LoginCommandsService _loginCommandsService;
    private readonly IGameMonitor _gameMonitor;
    private readonly ILogger<MainWindowViewModel> _logger;

    private string _statusText = "Ready";
    private bool _isLoading;
    private string _gameClientPath = string.Empty;
    private string _currentThemeName;
    private LaunchProfile? _currentProfile;
    private bool _applyingProfile;
    private string? _updateBannerText;
    private string? _updateDownloadUrl;
    private string? _updateReleaseUrl;
    private Version? _updateCurrentVersion;
    private Version? _updateRemoteVersion;

    /// <summary>Tracks PID → (Account, Server) for auto-relaunch.</summary>
    private readonly Dictionary<int, (Account Account, Server Server)> _launchedSessions = [];

    /// <summary>
    /// PIDs of relaunched clients that should be minimized once their character
    /// reaches <see cref="GameSessionStatus.InGame"/> (so we don't resize a
    /// window that's still on the login/character-select screen).
    /// </summary>
    private readonly HashSet<int> _pendingMinimizeOnInGame = [];

    private readonly DispatcherTimer _activeTimeTimer;

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
        ServerListDownloader serverListDownloader,
        BetaServerListDownloader betaServerListDownloader,
        TreeStatsService treeStatsService,
        ProfileService profileService,
        LoginCommandsService loginCommandsService,
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
        _serverListDownloader = serverListDownloader;
        _betaServerListDownloader = betaServerListDownloader;
        _treeStatsService = treeStatsService;
        _profileService = profileService;
        _loginCommandsService = loginCommandsService;
        _gameMonitor = gameMonitor;
        _logger = logger;

        _currentThemeName = _themeService.CurrentThemeName;
        _themeService.ThemeChanged += name =>
            System.Windows.Application.Current.Dispatcher.Invoke(() => CurrentThemeName = name);

        _logger.LogDebug("MainWindowViewModel initializing");

        _accountService.AccountsChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(ReloadAccountsAsync);
        _serverService.ServersChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(ReloadServersAsync);
        appCoordinator.ServerStatusRefreshed += (_, _) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(ReloadServersAsync);
        _gameMonitor.GameExited += (_, e) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnGameExited(e.ProcessId, e.WasMinimized));
        _gameMonitor.HeartbeatReceived += (_, e) =>
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnHeartbeatReceived(e));

        _activeTimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _activeTimeTimer.Tick += (_, _) => RefreshActiveTimeDisplay();
        _activeTimeTimer.Start();

        _gameClientPath = _config.GameClientPath;

        SelectedAccounts.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(CanLaunch)); SaveCurrentProfile(); };
        SelectedServers.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(CanLaunch)); SaveCurrentProfile(); };

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
        PreviousThemeCommand = new RelayCommand(PreviousTheme);
        NextThemeCommand = new RelayCommand(NextTheme);
        AddProfileCommand = new RelayCommand(AddProfile);

        foreach (var p in _profileService.Profiles)
            Profiles.Add(p);
        _currentProfile = _profileService.ActiveProfile;
    }

    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<Server> Servers { get; } = [];
    public ObservableCollection<GameSession> ActiveSessions { get; } = [];
    public ObservableCollection<LaunchProfile> Profiles { get; } = [];

    public ICommand AddProfileCommand { get; }

    public LaunchProfile? CurrentProfile
    {
        get => _currentProfile;
        set
        {
            if (_currentProfile?.Id == value?.Id) return;
            _currentProfile = value;
            OnPropertyChanged();
            if (value is not null)
                ApplyProfile(value);
        }
    }

    public string CurrentVersion { get; } =
        $"v{UpdateChecker.CurrentVersion.Major}.{UpdateChecker.CurrentVersion.Minor}.{UpdateChecker.CurrentVersion.Build}";

    public string? UpdateBannerText
    {
        get => _updateBannerText;
        private set { SetProperty(ref _updateBannerText, value); OnPropertyChanged(nameof(UpdateBannerVisibility)); }
    }

    public System.Windows.Visibility UpdateBannerVisibility =>
        string.IsNullOrEmpty(_updateBannerText) ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public void DismissUpdateBanner() => UpdateBannerText = null;

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

    public int MultiLaunchDelaySeconds
    {
        get => _config.MultiLaunchDelaySeconds;
        set
        {
            if (value < 1)
            {
                MessageBox.Show(
                    "Multi-launch delay must be at least 1 second.",
                    "Invalid Setting",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                OnPropertyChanged();
                return;
            }

            if (_config.MultiLaunchDelaySeconds != value)
            {
                _config.MultiLaunchDelaySeconds = value;
                _config.Save();
                OnPropertyChanged();
            }
        }
    }

    public bool CanLaunch => SelectedAccounts.Count > 0 && SelectedServers.Count > 0
        && !IsLoading && File.Exists(GameClientPath);

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
    public ICommand PreviousThemeCommand { get; }
    public ICommand NextThemeCommand { get; }

    public string CurrentThemeName
    {
        get => _currentThemeName;
        private set => SetProperty(ref _currentThemeName, value);
    }

    /// <summary>Raised when the view needs to restore ListBox selections from a profile.</summary>
    public event Action<IReadOnlyList<string>, IReadOnlyList<string>>? ProfileSelectionRestoreRequested;

    /// <summary>Raised after a server reload so the view can restore ListBox selection by ID.</summary>
    public event Action<IReadOnlyList<string>>? ServerSelectionRestoreRequested;
}
