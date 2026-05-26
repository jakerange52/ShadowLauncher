using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShadowLauncher.Application;
using ShadowLauncher.Infrastructure;
using ShadowLauncher.Infrastructure.Native; // AppUserModelId — also contains symlink types, re-enable SymlinkPrivilegeHelper usages with SymlinkLauncher

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

        // SymlinkPrivilegeHelper.PrivilegeStatus? symlinkStatus = null; // symlink
        // _coordinator.SymlinkPrivilegeChecked += (_, status) => symlinkStatus = status; // symlink
        _coordinator.AcBaseCopyRequired += (_, copyTask) =>
        {
            var copyWindow = new Presentation.Views.AcBaseCopyWindow();
            copyWindow.Show();
            _ = copyWindow.RunAsync(copyTask);
        };

        var firstRunService = _serviceProvider.GetRequiredService<FirstRunService>();
        _coordinator.FirewallRuleRequested += (_, _) =>
        {
            var result = MessageBox.Show(
                "ShadowLauncher creates a new folder for each game instance, which can cause " +
                "Windows Firewall to prompt you every time you launch.\n\n" +
                "Would you like to add a one-time firewall rule to prevent these prompts?\n\n" +
                "(This will show a UAC confirmation — nothing is changed without your approval.)",
                "Windows Firewall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                firstRunService.AddFirewallRule();
            else
                firstRunService.DeclineFirewallRule();
        };

        await _coordinator.InitializeAsync();

        // Apply saved theme (replaces the default ShadowTheme loaded from App.xaml).
        _serviceProvider.GetRequiredService<ThemeService>().ApplySaved();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        if (IsThwargLauncherRunning())
            ShowThwargRunningWarning();

        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // switch (symlinkStatus) // symlink
        // {
        //     case SymlinkPrivilegeHelper.PrivilegeStatus.GrantedNeedsLogoff: ShowSignOutPrompt(); break;
        //     case SymlinkPrivilegeHelper.PrivilegeStatus.GrantFailed:        ShowGrantFailedPrompt(); break;
        // }
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

    // private static void ShowSignOutPrompt() => MessageBox.Show( // symlink
    //     "ShadowLauncher has granted the symbolic link permission required for multi-client launching.\n\n" +
    //     "To activate it you must do a full sign-out:\n" +
    //     "  Start menu → click your name → Sign out\n\n" +
    //     "⚠️ Locking your screen (Win+L) and unlocking does NOT work — " +
    //     "Windows only updates your session token on a true sign-out.\n\n" +
    //     "After signing back in, relaunch ShadowLauncher normally.",
    //     "Sign Out Required", MessageBoxButton.OK, MessageBoxImage.Information);

    // private static void ShowGrantFailedPrompt() => MessageBox.Show( // symlink
    //     "ShadowLauncher could not set the symbolic link permission required for multi-client launching.\n\n" +
    //     "Steps to fix:\n" +
    //     "  1. Right-click ShadowLauncher → Run as administrator\n" +
    //     "  2. Let it start (it will grant the permission automatically)\n" +
    //     "  3. Close it, then do a full sign-out: Start menu → your name → Sign out\n" +
    //     "  4. Sign back in and relaunch ShadowLauncher normally\n\n" +
    //     "⚠️ Locking your screen and unlocking is NOT the same as signing out.",
    //     "Permission Required", MessageBoxButton.OK, MessageBoxImage.Warning);
}
