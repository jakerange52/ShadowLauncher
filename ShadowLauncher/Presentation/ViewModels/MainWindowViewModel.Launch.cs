using System.Windows;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private async Task LaunchGameAsync()
    {
        var accounts = SelectedAccounts.ToList();
        var servers = SelectedServers.ToList();
        if (accounts.Count == 0 || servers.Count == 0) return;

        _logger.LogDebug("Launch requested: {AccountCount} accounts × {ServerCount} servers",
            accounts.Count, servers.Count);

        var serversNeedingDats = new List<(Server Server, string DatSetId)>();
        foreach (var server in servers.DistinctBy(s => s.DatSetId))
        {
            if (string.IsNullOrWhiteSpace(server.DatSetId)) continue;
            if (!await _datSetService.IsDatSetReadyAsync(server.DatSetId))
                serversNeedingDats.Add((server, server.DatSetId));
        }

        var serversNeedingCustomDats = servers
            .DistinctBy(s => s.Id)
            .Where(s => (!string.IsNullOrWhiteSpace(s.CustomDatRegistryPath)
                      || !string.IsNullOrWhiteSpace(s.CustomDatZipUrl))
                     && !_datSetService.IsCustomDatCachePresent(s))
            .ToList();

        if (serversNeedingCustomDats.Count > 0 || serversNeedingDats.Count > 0)
        {
            var fetchWindow = new Presentation.Views.DatFetchWindow(
                System.Windows.Application.Current.MainWindow);
            fetchWindow.Show();

            try
            {
                foreach (var (fetchServer, datSetId) in serversNeedingDats)
                {
                    _logger.LogDebug("Fetching DAT set '{Id}' for server '{Server}'",
                        datSetId, fetchServer.Name);

                    var progress = new Progress<DatDownloadProgress>(
                        p => fetchWindow.ViewModel.Apply(p));

                    await _datSetService.DownloadMissingFilesAsync(datSetId, progress);
                }

                foreach (var fetchServer in serversNeedingCustomDats)
                {
                    _logger.LogDebug("Ensuring custom DAT source for server '{Server}'", fetchServer.Name);

                    var progress = new Progress<DatDownloadProgress>(
                        p => fetchWindow.ViewModel.Apply(p));

                    await _datSetService.EnsureCustomDatSourceReadyAsync(fetchServer, progress);
                }

                fetchWindow.ViewModel.SetComplete();
                await Task.Delay(600);
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

                StatusText = $"Launching {account.Name} on {server.Name}...";
                try
                {
                    var result = await _gameLauncher.LaunchGameAsync(account, server);
                    if (result.Success)
                    {
                        var session = await _sessionService.CreateSessionAsync(account, server, result.ProcessId);
                        ActiveSessions.Add(CloneSessionForUi(session));
                        _launchedSessions[result.ProcessId] = (account, server);
                        launched++;

                        var delay = _config.MultiLaunchDelaySeconds;
                        if (delay > 0 && (launched + failed + skipped) < accounts.Count * servers.Count)
                            await Task.Delay(TimeSpan.FromSeconds(delay));
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
}
