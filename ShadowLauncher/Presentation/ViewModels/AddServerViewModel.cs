using System.Windows.Input;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Presentation.ViewModels;

public class AddServerViewModel : ViewModelBase
{
    private EmulatorType _emulator = EmulatorType.ACE;
    private string _serverName = string.Empty;
    private string _description = string.Empty;
    private string _hostname = string.Empty;
    private string _port = "9000";
    private string _discordUrl = string.Empty;
    private string _websiteUrl = string.Empty;
    private bool _defaultRodat;
    private bool _secureLogon;
    private string _errorText = string.Empty;

    public AddServerViewModel()
    {
        SaveCommand = new RelayCommand(Save, () => CanSave);
    }

    public event EventHandler? SaveCompleted;

    public EmulatorType[] EmulatorTypes { get; } = Enum.GetValues<EmulatorType>();

    public EmulatorType Emulator
    {
        get => _emulator;
        set => SetProperty(ref _emulator, value);
    }

    public string ServerName
    {
        get => _serverName;
        set { if (SetProperty(ref _serverName, value)) OnPropertyChanged(nameof(CanSave)); }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Hostname
    {
        get => _hostname;
        set { if (SetProperty(ref _hostname, value)) OnPropertyChanged(nameof(CanSave)); }
    }

    public string Port
    {
        get => _port;
        set { if (SetProperty(ref _port, value)) OnPropertyChanged(nameof(CanSave)); }
    }

    public string DiscordUrl
    {
        get => _discordUrl;
        set => SetProperty(ref _discordUrl, value);
    }

    public string WebsiteUrl
    {
        get => _websiteUrl;
        set => SetProperty(ref _websiteUrl, value);
    }

    public bool DefaultRodat
    {
        get => _defaultRodat;
        set => SetProperty(ref _defaultRodat, value);
    }

    public bool SecureLogon
    {
        get => _secureLogon;
        set => SetProperty(ref _secureLogon, value);
    }

    public string ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value);
    }

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(ServerName) &&
        !string.IsNullOrWhiteSpace(Hostname) &&
        int.TryParse(Port, out var p) && p > 0 && p <= 65535;

    public ICommand SaveCommand { get; }

    public Server ToServer() => new()
    {
        Emulator = Emulator,
        Name = ServerName.Trim(),
        Description = Description.Trim(),
        Hostname = Hostname.Trim(),
        Port = int.Parse(Port),
        DiscordUrl = DiscordUrl.Trim(),
        WebsiteUrl = WebsiteUrl.Trim(),
        DefaultRodat = DefaultRodat,
        SecureLogon = SecureLogon
    };

    private void Save()
    {
        if (!CanSave)
        {
            ErrorText = "Server name, hostname, and a valid port are required.";
            return;
        }

        ErrorText = string.Empty;
        SaveCompleted?.Invoke(this, EventArgs.Empty);
    }
}
