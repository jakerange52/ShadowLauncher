using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Infrastructure.Persistence;

namespace ShadowLauncher.Application;

/// <summary>
/// Runs silently on first launch to pre-populate sensible defaults:
///   1. Detects the AC client path from the registry or standard install locations.
///   2. Imports accounts from ThwargLauncher if they exist and none are configured yet.
///
/// Nothing is overwritten if data already exists. All failures are swallowed so a
/// missing ThwargLauncher installation or unreadable files never block startup.
/// </summary>
public class FirstRunService
{
    private readonly IConfigurationProvider _config;
    private readonly AccountFileRepository _accountRepo;
    private readonly ILogger<FirstRunService> _logger;

    // Standard AC install locations to probe if registry lookup fails.
    private static readonly string[] KnownClientPaths =
    [
        @"C:\Program Files (x86)\Turbine\Asheron's Call\acclient.exe",
        @"C:\Program Files\Turbine\Asheron's Call\acclient.exe",
        @"C:\Program Files (x86)\Asheron's Call\acclient.exe",
        @"C:\Program Files\Asheron's Call\acclient.exe",
    ];

    // ThwargLauncher stores accounts in %LocalAppData%\ThwargLauncher\Accounts.txt
    private static readonly string ThwargAccountsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThwargLauncher", "Accounts.txt");

    public FirstRunService(
        IConfigurationProvider config,
        AccountFileRepository accountRepo,
        ILogger<FirstRunService> logger)
    {
        _config = config;
        _accountRepo = accountRepo;
        _logger = logger;
    }

    /// <summary>
    /// Runs all first-run detection. Safe to call on every startup — each check
    /// is guarded so it only acts when the target slot is still empty.
    /// </summary>
    public async Task RunAsync()
    {
        TryDetectGameClient();
        await TryImportThwargAccountsAsync();
    }

    // ── Game client detection ──────────────────────────────────────────────────

    private void TryDetectGameClient()
    {
        if (!string.IsNullOrWhiteSpace(_config.GameClientPath))
            return; // already configured

        var path = FindClientFromRegistry() ?? FindClientFromKnownPaths();
        if (path is null)
        {
            _logger.LogInformation("First-run: AC client not detected");
            return;
        }

        _config.GameClientPath = path;
        _config.Save();
        _logger.LogInformation("First-run: detected AC client at {Path}", path);
    }

    private static string? FindClientFromRegistry()
    {
        try
        {
            // Turbine's official installer writes to this key.
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Turbine\Asheron's Call");
            var installDir = key?.GetValue("InstallDir") as string;
            if (string.IsNullOrWhiteSpace(installDir)) return null;

            var exe = Path.Combine(installDir, "acclient.exe");
            return File.Exists(exe) ? exe : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindClientFromKnownPaths()
        => KnownClientPaths.FirstOrDefault(File.Exists);

    // ── ThwargLauncher account import ──────────────────────────────────────────

    private async Task TryImportThwargAccountsAsync()
    {
        try
        {
            var existing = await _accountRepo.GetAllAsync();
            if (existing.Any())
                return;

            if (!File.Exists(ThwargAccountsFile))
                return;

            var lines = await File.ReadAllLinesAsync(ThwargAccountsFile);
            var imported = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('\''))
                    continue;
                if (trimmed.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
                    continue;

var props = ThwargLineParser.Parse(trimmed);
                if (!props.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
                    continue;
                props.TryGetValue("Password", out var password);

                var account = new Core.Models.Account
                {
                    Id = name.ToLowerInvariant(),
                    Name = name,
                    PasswordHash = password ?? string.Empty,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow,
                };

                if (props.TryGetValue("Alias", out var alias) && !string.IsNullOrWhiteSpace(alias))
                    account.Notes = alias;

                await _accountRepo.AddAsync(account);
                imported++;
            }

            if (imported > 0)
                _logger.LogInformation("First-run: imported {Count} account(s) from ThwargLauncher", imported);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "First-run: could not import ThwargLauncher accounts");
        }
    }
}
