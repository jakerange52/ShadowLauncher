using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Infrastructure.Persistence;

namespace ShadowLauncher.Application;

/// <summary>
/// Runs silently on first launch to pre-populate sensible defaults:
///   1. Detects the AC client path from the registry or standard install locations.
///   2. Imports accounts and servers from ThwargLauncher if it has been used.
///
/// Nothing is overwritten if data already exists. All failures are swallowed so a
/// missing ThwargLauncher installation or unreadable files never block startup.
/// </summary>
public class FirstRunService
{
    private readonly IConfigurationProvider _config;
    private readonly AccountFileRepository _accountRepo;
    private readonly ServerFileRepository _serverRepo;
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

    // ThwargLauncher stores servers in %AppData%\ThwargLauncher\UserServerList.xml
    private static readonly string ThwargServersFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ThwargLauncher", "UserServerList.xml");

    public FirstRunService(
        IConfigurationProvider config,
        AccountFileRepository accountRepo,
        ServerFileRepository serverRepo,
        ILogger<FirstRunService> logger)
    {
        _config = config;
        _accountRepo = accountRepo;
        _serverRepo = serverRepo;
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
        await TryImportThwargServersAsync();
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

                var props = ParseThwargLine(trimmed);
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

    // ── ThwargLauncher server import ───────────────────────────────────────────

    private async Task TryImportThwargServersAsync()
    {
        try
        {
            var existing = await _serverRepo.GetAllAsync();
            if (existing.Any())
                return;

            if (!File.Exists(ThwargServersFile))
                return;

            var doc = XDocument.Load(ThwargServersFile);
            var imported = 0;

            foreach (var item in doc.Descendants("ServerItem"))
            {
                var name = item.Element("name")?.Value ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var connectString = item.Element("connect_string")?.Value ?? string.Empty;
                string hostname;
                int port = 9000;

                if (connectString.Contains(':'))
                {
                    var parts = connectString.Split(':', 2);
                    hostname = parts[0];
                    int.TryParse(parts[1], out port);
                }
                else
                {
                    hostname = item.Element("server_host")?.Value ?? connectString;
                    int.TryParse(item.Element("server_port")?.Value, out port);
                }

                if (string.IsNullOrWhiteSpace(hostname)) continue;

                var server = new Core.Models.Server
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Hostname = hostname,
                    Port = port == 0 ? 9000 : port,
                    Description = item.Element("description")?.Value ?? string.Empty,
                    DiscordUrl = item.Element("discord_url")?.Value ?? string.Empty,
                    WebsiteUrl = item.Element("website_url")?.Value ?? string.Empty,
                    DefaultRodat = item.Element("default_rodat")?.Value
                        ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
                    SecureLogon = item.Element("default_secure")?.Value
                        ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
                    IsManuallyAdded = true,
                };

                await _serverRepo.AddAsync(server);
                imported++;
            }

            if (imported > 0)
                _logger.LogInformation("First-run: imported {Count} server(s) from ThwargLauncher", imported);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "First-run: could not import ThwargLauncher servers");
        }
    }

    // ── Shared parsing ─────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a ThwargLauncher account line.
    /// Thwarg escapes commas as ^c, equals as ^e, and carets as ^u.
    /// </summary>
    private static Dictionary<string, string> ParseThwargLine(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairs = line.Split(',');

        foreach (var pair in pairs)
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx < 0) continue;

            var key = pair[..eqIdx].Trim();
            var value = pair[(eqIdx + 1)..]
                .Replace("^c", ",")
                .Replace("^e", "=")
                .Replace("^u", "^");

            result[key] = value;
        }

        return result;
    }
}
