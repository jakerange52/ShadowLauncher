using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Configuration;
using ShadowLauncher.Infrastructure.Events;
using ShadowLauncher.Infrastructure.FileSystem;
using ShadowLauncher.Infrastructure.Logging;
using ShadowLauncher.Infrastructure.Persistence;
using ShadowLauncher.Infrastructure.Updates;
using ShadowLauncher.Infrastructure.WebServices;
using ShadowLauncher.Infrastructure;
using ShadowLauncher.Presentation.ViewModels;
using ShadowLauncher.Services.Accounts;
using ShadowLauncher.Services.GameSessions;
using ShadowLauncher.Services.Launching;
using ShadowLauncher.Services.Monitoring;
using ShadowLauncher.Services.Servers;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Application;

public static class ServiceBootstrapper
{
    public static IServiceCollection RegisterServices(this IServiceCollection services)
    {
        // Configuration
        services.AddSingleton<IConfigurationProvider>(sp =>
        {
            var config = new AppConfiguration();
            config.Load();
            return config;
        });

        // Infrastructure
        services.AddSingleton<IEventAggregator, EventAggregator>();
        services.AddSingleton<IHeartbeatReader, HeartbeatReader>();
        services.AddSingleton<ServerListDownloader>();
        services.AddSingleton<UpdateChecker>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<DatRegistryDownloader>();
        services.AddSingleton<IDatSetService, DatSetService>();
        services.AddSingleton<ShadowLauncher.Infrastructure.Native.SymlinkLauncher>();

        // Repositories
        services.AddSingleton<AccountFileRepository>(sp =>
        {
            var config = sp.GetRequiredService<IConfigurationProvider>();
            return new AccountFileRepository(
                Path.Combine(config.DataDirectory, "Accounts.txt"));
        });
        services.AddSingleton<IRepository<Account>>(sp => sp.GetRequiredService<AccountFileRepository>());
        services.AddSingleton<ServerFileRepository>(sp =>
        {
            var config = sp.GetRequiredService<IConfigurationProvider>();
            return new ServerFileRepository(
                Path.Combine(config.DataDirectory, "UserServerList.xml"));
        });
        services.AddSingleton<IRepository<Server>>(sp => sp.GetRequiredService<ServerFileRepository>());

        // Services
        services.AddSingleton<IAccountService, AccountService>();
        services.AddSingleton<IServerService, ServerService>();
        services.AddSingleton<IGameSessionService, GameSessionService>();
        services.AddSingleton<IGameLauncher, GameLauncher>();
        services.AddSingleton<IGameMonitor, GameMonitor>();

        // Application
        services.AddSingleton<AppCoordinator>();

        // Presentation
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
            builder.AddProvider(new FileLoggerProvider(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ShadowLauncher", "Logs"),
                retentionDays: 7));
        });

        return services;
    }
}
