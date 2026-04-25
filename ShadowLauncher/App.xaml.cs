using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShadowLauncher.Application;
using ShadowLauncher.Infrastructure;

namespace ShadowLauncher;

public partial class App : System.Windows.Application
{
    private IServiceProvider? _serviceProvider;
    private AppCoordinator? _coordinator;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Must match the AppUserModelId set on the Start Menu shortcut so that
        // taskbar pins survive upgrades (Windows tracks the pin by this stable ID).
        SetCurrentProcessExplicitAppUserModelID("ShadowLauncher.App");

        var services = new ServiceCollection();
        services.RegisterServices();
        _serviceProvider = services.BuildServiceProvider();

        _coordinator = _serviceProvider.GetRequiredService<AppCoordinator>();
        await _coordinator.InitializeAsync();

        // Apply saved theme (replaces the default ShadowTheme loaded from App.xaml).
        var themeService = _serviceProvider.GetRequiredService<ThemeService>();
        themeService.ApplySaved();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        if (Process.GetProcessesByName("ThwargLauncher").Length > 0)
        {
            MessageBox.Show(
                "ThwargLauncher is currently running.\n\n" +
                "ShadowLauncher and ThwargLauncher both use ThwargFilter and can cause a corrupted state in Decal plugins when run simultaneously.\n\n" +
                "It is recommended to close ThwargLauncher before continuing.",
                "Compatibility Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_coordinator is not null)
            await _coordinator.ShutdownAsync();

        (_serviceProvider as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
