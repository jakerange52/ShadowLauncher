using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Infrastructure;
using ShadowLauncher.Infrastructure.Updates;
using ShadowLauncher.Presentation.Views;

namespace ShadowLauncher.Presentation.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationProvider _config;
    private readonly UpdateChecker _updateChecker;
    private readonly ThemeService _themeService;
    private string _decalPath;
    private string _statusText = string.Empty;
    private int _downloadProgress;
    private bool _isDownloading;
    private string _currentThemeName;
    private bool _datDeveloperMode;
    private bool _attemptDecalInjection;
    private int _multiLaunchDelaySeconds;
    private CancellationTokenSource? _downloadCts;

    public SettingsViewModel(IConfigurationProvider config, UpdateChecker updateChecker, ThemeService themeService)
    {
        _config = config;
        _updateChecker = updateChecker;
        _themeService = themeService;
        _decalPath = _config.DecalPath;
        _currentThemeName = _themeService.CurrentThemeName;
        _datDeveloperMode = _config.DatDeveloperMode;
        _attemptDecalInjection = _config.AttemptDecalInjection;
        _multiLaunchDelaySeconds = _config.MultiLaunchDelaySeconds;

        SaveCommand = new RelayCommand(Save);
        BrowseDecalCommand = new RelayCommand(() => BrowseRequested?.Invoke(this, nameof(DecalPath)));
        OpenUserFileCommand = new RelayCommand(OpenUserFile);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        PreviousThemeCommand = new RelayCommand(PreviousTheme);
        NextThemeCommand = new RelayCommand(NextTheme);
    }

    public event EventHandler<string>? BrowseRequested;
    public event EventHandler? SaveCompleted;

    public string CurrentVersion { get; } =
        $"v{UpdateChecker.CurrentVersion.Major}.{UpdateChecker.CurrentVersion.Minor}.{UpdateChecker.CurrentVersion.Build}";

    public string DatSetsDirectory => _config.DatSetsDirectory;

    public string DecalPath
    {
        get => _decalPath;
        set => SetProperty(ref _decalPath, value);
    }

    public bool DatDeveloperMode
    {
        get => _datDeveloperMode;
        set => SetProperty(ref _datDeveloperMode, value);
    }

    public bool AttemptDecalInjection
    {
        get => _attemptDecalInjection;
        set => SetProperty(ref _attemptDecalInjection, value);
    }

    public int MultiLaunchDelaySeconds
    {
        get => _multiLaunchDelaySeconds;
        set => SetProperty(ref _multiLaunchDelaySeconds, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>0–100 download progress for the update installer.</summary>
    public int DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    /// <summary>True while the installer is being downloaded — hides the check button.</summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand BrowseDecalCommand { get; }
    public ICommand OpenUserFileCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
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
        CurrentThemeName = _themeService.CurrentThemeName;
        _config.Theme = _currentThemeName;
        _config.Save();
    }

    private void NextTheme()
    {
        _themeService.Next();
        CurrentThemeName = _themeService.CurrentThemeName;
        _config.Theme = _currentThemeName;
        _config.Save();
    }

    private void Save()
    {
        if (MultiLaunchDelaySeconds < 1)
        {
            MessageBox.Show(
                "Multi-launch delay must be at least 1 second.",
                "Invalid Setting",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        _config.DecalPath = DecalPath;
        _config.Theme = _currentThemeName;
        _config.DatDeveloperMode = DatDeveloperMode;
        _config.AttemptDecalInjection = AttemptDecalInjection;
        _config.MultiLaunchDelaySeconds = MultiLaunchDelaySeconds;
        _config.Save();

        StatusText = "Settings saved.";
        SaveCompleted?.Invoke(this, EventArgs.Empty);
    }

    private async Task CheckForUpdatesAsync()
    {
        StatusText = "Checking for updates...";
        var result = await _updateChecker.CheckAsync();

        if (!result.Success)
        {
            StatusText = $"Update check failed: {result.ErrorMessage}";
            return;
        }

        if (!result.UpdateAvailable)
        {
            StatusText = $"You are up to date (v{result.CurrentVersion})";
            return;
        }

        if (!UpdateAvailableWindow.PromptInstall(result.CurrentVersion, result.RemoteVersion, result.ReleaseUrl))
            return;

        if (string.IsNullOrWhiteSpace(result.DownloadUrl))
        {
            StatusText = "No installer asset found in the latest release.";
            return;
        }

        await DownloadAndApplyAsync(result.DownloadUrl);
    }

    private async Task DownloadAndApplyAsync(string url)
    {
        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = "Downloading update...";
        _downloadCts = new CancellationTokenSource();

        string installerPath;
        try
        {
            var progress = new Progress<int>(pct =>
            {
                DownloadProgress = pct;
                StatusText = $"Downloading... {pct}%";
            });

            installerPath = await _updateChecker.DownloadInstallerAsync(
                url, progress, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Update cancelled.";
            IsDownloading = false;
            return;
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
            IsDownloading = false;
            return;
        }

        StatusText = "Download complete — launching installer...";

        // Shut this instance down, run the installer silently, then relaunch.
        // We spin up a detached helper process (cmd) to wait for the installer
        // to finish before starting the new exe — we can't wait ourselves because
        // we're about to exit.
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var relaunchCmd = $"/c start \"\" /wait \"{installerPath}\" /install /quiet /norestart & del /f /q \"{installerPath}\" & start \"\" \"{exePath}\"";

        Process.Start(new ProcessStartInfo
        {
            FileName        = "cmd.exe",
            Arguments       = relaunchCmd,
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden,
        });

        System.Windows.Application.Current.Shutdown();
    }

    private void OpenUserFile()
    {
        var path = Path.Combine(_config.DataDirectory, "accounts.txt");
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else
            StatusText = "Accounts file not found.";
    }
}
