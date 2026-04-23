using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Infrastructure.Updates;

namespace ShadowLauncher.Presentation.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationProvider _config;
    private readonly UpdateChecker _updateChecker;
    private string _decalPath;
    private string _statusText = string.Empty;
    private int _downloadProgress;
    private bool _isDownloading;
    private CancellationTokenSource? _downloadCts;

    public SettingsViewModel(IConfigurationProvider config, UpdateChecker updateChecker)
    {
        _config = config;
        _updateChecker = updateChecker;
        _decalPath = _config.DecalPath;

        SaveCommand = new RelayCommand(Save);
        BrowseDecalCommand = new RelayCommand(() => BrowseRequested?.Invoke(this, nameof(DecalPath)));
        OpenUserFileCommand = new RelayCommand(OpenUserFile);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
    }

    public event EventHandler<string>? BrowseRequested;
    public event EventHandler? SaveCompleted;

    public string CurrentVersion { get; } =
        $"v{UpdateChecker.CurrentVersion.Major}.{UpdateChecker.CurrentVersion.Minor}.{UpdateChecker.CurrentVersion.Build}";

    public string DecalPath
    {
        get => _decalPath;
        set => SetProperty(ref _decalPath, value);
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

    private void Save()
    {
        _config.DecalPath = DecalPath;
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

        // Update available — ask the user whether to install now.
        var answer = MessageBox.Show(
            $"ShadowLauncher v{result.RemoteVersion} is available (you have v{result.CurrentVersion}).\n\n" +
            (string.IsNullOrWhiteSpace(result.ReleaseNotes) ? string.Empty : result.ReleaseNotes.Trim() + "\n\n") +
            "Download and install now? The app will restart automatically.",
            "Update Available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.Yes) return;

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

        // Launch the new bundle installer. /install /quiet installs silently;
        // /norestart suppresses any reboot prompt from the .NET runtime package.
        // The bundle's built-in close logic will shut down any running ShadowLauncher
        // processes before overwriting files.
        Process.Start(new ProcessStartInfo
        {
            FileName        = installerPath,
            Arguments       = "/install /quiet /norestart",
            UseShellExecute = true,
        });

        // Shut this instance down so the installer can overwrite the files.
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
