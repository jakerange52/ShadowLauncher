using System.Windows;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure;
using ShadowLauncher.Infrastructure.WebServices;
using ShadowLauncher.Presentation.Views;

namespace ShadowLauncher.Presentation.ViewModels;

public partial class MainWindowViewModel
{
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
        _logger.LogDebug("Loading accounts, servers, and sessions...");

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
            {
                ActiveSessions.Add(CloneSessionForUi(session));

                if (!_launchedSessions.ContainsKey(session.ProcessId))
                {
                    var account = Accounts.FirstOrDefault(a =>
                        string.Equals(a.Id, session.AccountId, StringComparison.OrdinalIgnoreCase));
                    var server = Servers.FirstOrDefault(s =>
                        string.Equals(s.Id, session.ServerId, StringComparison.OrdinalIgnoreCase));
                    if (account is not null && server is not null)
                    {
                        _launchedSessions[session.ProcessId] = (account, server);
                        _logger.LogDebug("Adopted session PID {Pid} registered for auto-relaunch ({Account} on {Server})",
                            session.ProcessId, account.Name, server.Name);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Adopted session PID {Pid} could not be registered for auto-relaunch — account '{AccountId}' or server '{ServerId}' not found",
                            session.ProcessId, session.AccountId, session.ServerId);
                    }
                }
            }

            StatusText = $"Loaded {Accounts.Count} accounts, {Servers.Count} servers";

            if (_currentProfile is not null)
                ProfileSelectionRestoreRequested?.Invoke(
                    _currentProfile.SelectedAccountIds,
                    _currentProfile.SelectedServerIds);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }

        _ = CheckForUpdateSilentlyAsync();
    }

    private async Task ReloadAccountsAsync()
    {
        _logger.LogDebug("Reloading accounts from file...");
        Accounts.Clear();
        foreach (var account in await _accountService.GetAllAccountsAsync())
            Accounts.Add(account);
        StatusText = $"Accounts reloaded ({Accounts.Count} accounts)";
    }

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
        var window = new AddAccountWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            try
            {
                var account = await _accountService.CreateAccountAsync(vm.Username, vm.Password, vm.PreferencePath);
                Accounts.Add(account);
                StatusText = $"Account '{account.Name}' added.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error adding account: {ex.Message}";
            }
        }
    }

    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_config, _updateChecker, _themeService);
        var window = new SettingsWindow(vm, _accountService, _serverService, _loginCommandsService, _profileService);
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ProfilesEdited += (_, _) => RefreshProfiles();
        if (window.ShowDialog() == true)
            StatusText = "Settings saved.";
    }

    private async Task AddServerAsync()
    {
        var vm = new AddServerViewModel(_config);
        var window = new AddServerWindow(vm)
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

    public async Task EditAccountAsync(Account account)
    {
        var vm = new EditAccountViewModel(account);
        var window = new EditAccountWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() == true)
        {
            try
            {
                await _accountService.UpdateAccountAsync(account);
                await ReloadAccountsAsync();
                StatusText = $"Account '{account.Name}' updated.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error updating account: {ex.Message}";
            }
        }
    }

    private void BrowseServers()
    {
        var vm = new BrowseServersViewModel(_serverListDownloader, _betaServerListDownloader, _treeStatsService);
        vm.ServerAdded += async (_, server) =>
        {
            try
            {
                var copy = new Server
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
                    IsBeta = server.IsBeta,
                    CustomDatZipUrl = server.CustomDatZipUrl,
                    DatSetId = server.DatSetId
                        ?? await _datSetService.ResolveDatSetIdForServerAsync(server.Name)
                };

                var added = await _serverService.CreateServerAsync(copy);
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

        var window = new BrowseServersWindow(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        window.ShowDialog();
    }
}
