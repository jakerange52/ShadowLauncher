using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShadowLauncher.Application;
using ShadowLauncher.Infrastructure;
using ShadowLauncher.Infrastructure.Native;

namespace ShadowLauncher;

public partial class App : System.Windows.Application
{
    private const string AppUserModelIdValue = "ShadowLauncher.App";

    private ServiceProvider? _serviceProvider;
    private AppCoordinator? _coordinator;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { await StartupCoreAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show($"ShadowLauncher failed to start:\n\n{ex}", "Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartupCoreAsync()
    {
        // Prevent WPF from shutting down if a pre-main-window dialog closes early
        // (e.g. AcBaseCopyWindow). Restored to OnMainWindowClose after main window shows.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppUserModelId.Set(AppUserModelIdValue);

        var services = new ServiceCollection();
        services.RegisterServices();
        _serviceProvider = services.BuildServiceProvider();

        _coordinator = _serviceProvider.GetRequiredService<AppCoordinator>();

        _coordinator.AcBaseCopyRequired += (_, copyTask) =>
        {
            var copyWindow = new Presentation.Views.AcBaseCopyWindow();
            copyWindow.Show();
            _ = copyWindow.RunAsync(copyTask);
        };

        await _coordinator.InitializeAsync();

        // Apply saved theme (replaces the default ShadowTheme loaded from App.xaml).
        _serviceProvider.GetRequiredService<ThemeService>().ApplySaved();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        if (IsThwargLauncherRunning())
            ShowThwargRunningWarning();

        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_coordinator is not null)
                await _coordinator.ShutdownAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shutdown error: {ex}");
        }
        finally
        {
            _serviceProvider?.Dispose();
            base.OnExit(e);
        }
    }

    private static bool IsThwargLauncherRunning()
    {
        var processes = Process.GetProcessesByName("ThwargLauncher");
        try { return processes.Length > 0; }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    private static void ShowThwargRunningWarning() => MessageBox.Show(
        "ThwargLauncher is currently running.\n\n" +
        "ShadowLauncher and ThwargLauncher both use ThwargFilter and can cause a corrupted state in Decal plugins when run simultaneously.\n\n" +
        "It is recommended to close ThwargLauncher before continuing.",
        "Compatibility Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
}
