using System.Windows;
using Microsoft.Win32;
using ShadowLauncher.Presentation.ViewModels;
using ShadowLauncher.Services.Accounts;
using ShadowLauncher.Services.LoginCommands;
using ShadowLauncher.Services.Servers;

namespace ShadowLauncher.Presentation.Views;

public partial class SettingsWindow : Window
{
    private readonly IAccountService? _accountService;
    private readonly IServerService? _serverService;
    private readonly LoginCommandsService? _loginCommandsService;

    public SettingsWindow(SettingsViewModel viewModel, IAccountService? accountService = null, IServerService? serverService = null, LoginCommandsService? loginCommandsService = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _accountService = accountService;
        _serverService = serverService;
        _loginCommandsService = loginCommandsService;

        viewModel.BrowseRequested += OnBrowseRequested;
        viewModel.SaveCompleted += (_, _) => DialogResult = true;

        Loaded += (_, _) => OffsetFromOwner();
        Closed += (_, _) => WindowPositionHelper.Save(this);
    }

    /// <summary>Raised after global or per-character login commands are saved.</summary>
    public event EventHandler? LoginCommandsSaved;

    private void OffsetFromOwner()
    {
        if (Owner is null) return;
        WindowPositionHelper.RestoreOrOffset(this, Owner);
    }

    private void OnBrowseRequested(object? sender, string propertyName)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "DLL Files|*.dll|All Files|*.*",
            Title = "Select Decal Inject DLL"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var vm = (SettingsViewModel)DataContext;
            vm.DecalPath = dialog.FileName;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenLoginCommands_Click(object sender, RoutedEventArgs e)
    {
        var service = _loginCommandsService ?? new LoginCommandsService();
        var window = new LoginCommandsWindow(service)
        {
            Owner = Owner ?? this
        };
        if (window.ShowDialog() == true)
            LoginCommandsSaved?.Invoke(this, EventArgs.Empty);
    }

    private void OpenPerCharacterLoginCommands_Click(object sender, RoutedEventArgs e)
    {
        if (_accountService is null || _serverService is null) return;
        var service = _loginCommandsService ?? new LoginCommandsService();
        var window = new PerCharacterLoginCommandsWindow(service, _accountService, _serverService)
        {
            Owner = Owner ?? this
        };
        window.Closed += (_, _) => LoginCommandsSaved?.Invoke(this, EventArgs.Empty);
        window.ShowDialog();
    }

    private void OpenHelp_Click(object sender, RoutedEventArgs e)
    {
        var window = new HelpWindow
        {
            Owner = Owner ?? this
        };
        window.ShowDialog();
    }
}
