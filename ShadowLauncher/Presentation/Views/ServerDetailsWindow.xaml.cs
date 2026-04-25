using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Presentation.ViewModels;

namespace ShadowLauncher.Presentation.Views;

public partial class ServerDetailsWindow : Window
{
    private readonly Server _server;
    private readonly bool _datDeveloperMode;
    private readonly IConfigurationProvider _config;
    public event EventHandler<Server>? ServerEdited;

    public ServerDetailsWindow(Server server, Window owner, IConfigurationProvider config)
    {
        InitializeComponent();
        Owner = owner;
        _server = server;
        _config = config;
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
