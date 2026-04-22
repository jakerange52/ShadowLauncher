using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShadowLauncher.Application;

namespace ShadowLauncher;

public partial class App : System.Windows.Application
{
    private IServiceProvider? _serviceProvider;
    private AppCoordinator? _coordinator;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.RegisterServices();
        _serviceProvider = services.BuildServiceProvider();

        _coordinator = _serviceProvider.GetRequiredService<AppCoordinator>();
        await _coordinator.InitializeAsync();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
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
