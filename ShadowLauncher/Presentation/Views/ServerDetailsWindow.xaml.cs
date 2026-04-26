using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Presentation.ViewModels;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Presentation.Views;

public partial class ServerDetailsWindow : Window
{
    private readonly Server _server;
    private readonly bool _datDeveloperMode;
    private readonly IConfigurationProvider _config;
    private readonly IDatSetService? _datSetService;
    public event EventHandler<Server>? ServerEdited;

    public ServerDetailsWindow(Server server, Window owner, IConfigurationProvider config, IDatSetService? datSetService = null)
    {
        InitializeComponent();
        Owner = owner;
        _server = server;
        _config = config;
        _datSetService = datSetService;
        _datDeveloperMode = config.DatDeveloperMode;
        Loaded += (_, _) => { Populate(); AddAccountWindow.ClampedOffset(this, owner); };
    }

    private void Populate()
    {
        // Header
        ServerNameLabel.Text = _server.Name;
        EmuLabel.Text = _server.Emulator.ToString();

        // Status dot colour
        StatusDot.Fill = _server.IsOnline
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
            : new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));

        // Description
        if (!string.IsNullOrWhiteSpace(_server.Description))
        {
            DescriptionLabel.Text = _server.Description;
            DescriptionLabel.Visibility = Visibility.Visible;
        }

        // Host:Port
        HostLabel.Text = $"{_server.Hostname}:{_server.Port}";

        // Published status
        if (!string.IsNullOrWhiteSpace(_server.PublishedStatus))
        {
            PublishedStatusLabel.Text = _server.PublishedStatus;
        }
        else
        {
            PublishedStatusLabel.Text = _server.IsOnline ? "Online" : "Offline";
            PublishedStatusLabel.Foreground = _server.IsOnline
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        }

        // Flags
        RodatLabel.Text = _server.DefaultRodat ? "On" : "Off";
        SecureLogonLabel.Text = _server.SecureLogon ? "Yes" : "No";

        // Links
        bool hasDiscord = !string.IsNullOrWhiteSpace(_server.DiscordUrl);
        bool hasWebsite = !string.IsNullOrWhiteSpace(_server.WebsiteUrl)
            && !string.Equals(_server.WebsiteUrl, _server.DiscordUrl, StringComparison.OrdinalIgnoreCase);

        if (hasDiscord)
            DiscordButton.Visibility = Visibility.Visible;

        if (hasWebsite)
            WebsiteButton.Visibility = Visibility.Visible;

        if (hasDiscord || hasWebsite)
        {
            LinksDivider.Visibility = Visibility.Visible;
            LinksPanel.Visibility = Visibility.Visible;
        }

        // Edit button: only shown when Dat Developer Mode is on AND server was manually added
        if (_datDeveloperMode && _server.IsManuallyAdded)
        {
            EditDivider.Visibility = Visibility.Visible;
            EditPanel.Visibility = Visibility.Visible;
        }

        // DAT refresh: shown for any server whose DATs are managed by the launcher —
        // either a registry DatSetId, a custom zip URL (including beta servers), or both.
        // Not shown for local CustomDatRegistryPath servers (user manages those files directly).
        bool hasManagedDats = _datSetService is not null
            && (!string.IsNullOrWhiteSpace(_server.DatSetId) || !string.IsNullOrWhiteSpace(_server.CustomDatZipUrl))
            && string.IsNullOrWhiteSpace(_server.CustomDatRegistryPath);

        if (hasManagedDats)
        {
            DatSetLabel.Text = !string.IsNullOrWhiteSpace(_server.DatSetId)
                ? $"DAT set: {_server.DatSetId}"
                : $"DAT source: {_server.Name}";
            DatRefreshDivider.Visibility = Visibility.Visible;
            DatRefreshPanel.Visibility = Visibility.Visible;
        }
    }

    private async void RefreshDats_Click(object sender, RoutedEventArgs e)
    {
        if (_datSetService is null) return;

        // Determine what label to show in the confirmation dialog
        var datLabel = !string.IsNullOrWhiteSpace(_server.DatSetId)
            ? $"'{_server.DatSetId}'"
            : $"'{_server.Name}'";

        var result = MessageBox.Show(
            $"This will delete the locally cached DAT files for {datLabel} and re-download them.\n\nContinue?",
            "Refresh DATs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // Resolve the correct cache directory for this server
        var cacheDir = _datSetService.GetLocalDatSetPathForServer(_server);
        if (Directory.Exists(cacheDir))
        {
            try
            {
                Directory.Delete(cacheDir, recursive: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not delete cached DAT files:\n\n{ex.Message}",
                    "Refresh DATs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        // Re-download using the appropriate service method
        var fetchWindow = new DatFetchWindow(this);
        fetchWindow.Show();
        try
        {
            var progress = new Progress<DatDownloadProgress>(p => fetchWindow.ViewModel.Apply(p));

            if (!string.IsNullOrWhiteSpace(_server.CustomDatZipUrl))
            {
                await _datSetService.EnsureCustomDatSourceReadyAsync(_server, progress);

                // Complete the cache with retail DATs for any files not in the zip,
                // matching what SymlinkLauncher does at launch so IsCustomDatCachePresent
                // returns true immediately and the fetch window won't reopen on next launch.
                var clientDir = Path.GetDirectoryName(_config.GameClientPath);
                if (!string.IsNullOrWhiteSpace(clientDir))
                {
                    var cacheDir2 = _datSetService.GetLocalDatSetPathForServer(_server);
                    await _datSetService.CompleteDatCacheFromRetailAsync(cacheDir2, clientDir);
                }
            }
            else
                await _datSetService.DownloadMissingFilesAsync(_server.DatSetId!, progress);

            fetchWindow.ViewModel.SetComplete();
            await Task.Delay(600);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"DAT download failed:\n\n{ex.Message}",
                "Refresh DATs",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            fetchWindow.Close();
        }
    }

    private void EditServer_Click(object sender, RoutedEventArgs e)
    {
        var vm = new AddServerViewModel(_config);
        vm.LoadFromServer(_server);

        var editWindow = new AddServerWindow(vm, "Edit Server") { Owner = this };
        if (editWindow.ShowDialog() == true)
        {
            var updated = vm.ApplyToServer(_server);
            ServerEdited?.Invoke(this, updated);
            Close();
        }
    }

    private void Discord_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_server.DiscordUrl))
            Process.Start(new ProcessStartInfo(_server.DiscordUrl) { UseShellExecute = true });
    }

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_server.WebsiteUrl))
            Process.Start(new ProcessStartInfo(_server.WebsiteUrl) { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
