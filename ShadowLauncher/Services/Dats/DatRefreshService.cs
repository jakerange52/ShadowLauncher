using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Presentation.Views;

namespace ShadowLauncher.Services.Dats;

/// <summary>
/// Deletes a server's cached DAT directory and re-downloads from registry or custom zip.
/// Shared by Server Details "Refresh DATs".
/// </summary>
public class DatRefreshService
{
    private readonly IDatSetService _datSetService;
    private readonly IConfigurationProvider _config;
    private readonly ILogger<DatRefreshService> _logger;

    public DatRefreshService(
        IDatSetService datSetService,
        IConfigurationProvider config,
        ILogger<DatRefreshService> logger)
    {
        _datSetService = datSetService;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Deletes the cache for <paramref name="server"/> and re-downloads. Shows
    /// <see cref="DatFetchWindow"/> progress on the UI thread.
    /// </summary>
    public async Task RefreshManagedDatSetAsync(Server server, Window owner)
    {
        var cacheDir = _datSetService.GetLocalDatSetPathForServer(server);
        if (Directory.Exists(cacheDir))
        {
            try
            {
                Directory.Delete(cacheDir, recursive: true);
                _logger.LogInformation("Deleted DAT cache at {Dir} for server '{Server}'", cacheDir, server.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete DAT cache at {Dir}", cacheDir);
                MessageBox.Show(
                    owner,
                    $"Could not delete cached DAT files:\n\n{ex.Message}",
                    "Refresh DATs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                throw;
            }
        }

        var fetchWindow = new DatFetchWindow(owner);
        fetchWindow.Show();
        try
        {
            var progress = new Progress<DatDownloadProgress>(p => fetchWindow.ViewModel.Apply(p));

            if (!string.IsNullOrWhiteSpace(server.CustomDatZipUrl))
            {
                await _datSetService.EnsureCustomDatSourceReadyAsync(server, progress);

                var clientDir = Path.GetDirectoryName(_config.GameClientPath);
                if (!string.IsNullOrWhiteSpace(clientDir))
                {
                    var cacheDir2 = _datSetService.GetLocalDatSetPathForServer(server);
                    await _datSetService.CompleteDatCacheFromRetailAsync(cacheDir2, clientDir);
                }
            }
            else
            {
                await _datSetService.DownloadMissingFilesAsync(server.DatSetId!, progress);
            }

            fetchWindow.ViewModel.SetComplete();
            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DAT refresh failed for server '{Server}'", server.Name);
            MessageBox.Show(
                owner,
                $"DAT download failed:\n\n{ex.Message}",
                "Refresh DATs",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            throw;
        }
        finally
        {
            fetchWindow.Close();
        }
    }
}
