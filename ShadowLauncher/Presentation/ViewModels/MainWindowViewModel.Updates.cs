using System.Windows;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Infrastructure.Updates;
using ShadowLauncher.Presentation.Views;

namespace ShadowLauncher.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    public async Task ApplyUpdateAsync()
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;

        if (!UpdateAvailableWindow.PromptInstall(_updateCurrentVersion, _updateRemoteVersion, _updateReleaseUrl ?? string.Empty))
            return;

        DismissUpdateBanner();

        var progress = new Progress<int>(pct => StatusText = $"Downloading update... {pct}%");
        string installerPath;
        try
        {
            installerPath = await _updateChecker.DownloadInstallerAsync(_updateDownloadUrl, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            StatusText = $"Download failed: {ex.Message}";
            return;
        }

        StatusText = "Download complete — launching installer...";
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        var relaunchCmd = $"/c start \"\" /wait \"{installerPath}\" /install /quiet /norestart & del /f /q \"{installerPath}\" & start \"\" \"{exePath}\"";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "cmd.exe",
            Arguments       = relaunchCmd,
            UseShellExecute = true,
            WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden,
        });
        System.Windows.Application.Current.Shutdown();
    }

    private async Task CheckForUpdateSilentlyAsync()
    {
        try
        {
            var result = await _updateChecker.CheckAsync();
            if (result.Success && result.UpdateAvailable)
            {
                _updateDownloadUrl = result.DownloadUrl;
                _updateReleaseUrl = result.ReleaseUrl;
                _updateCurrentVersion = result.CurrentVersion;
                _updateRemoteVersion = result.RemoteVersion;
                UpdateBannerText = $"\u2B06 New version available: v{result.RemoteVersion} \u2014 you have v{result.CurrentVersion}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Silent update check failed");
        }
    }
}
