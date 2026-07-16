using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Infrastructure.Paths;
using ShadowLauncher.Infrastructure.Persistence;

namespace ShadowLauncher.Application;

public record AcBaseCopyProgress(int FilesCompleted, int FilesTotal, string CurrentFile);

/// <summary>
/// Runs silently on first launch to pre-populate sensible defaults:
///   1. Detects the AC client path from the registry or standard install locations.
///   2. Imports accounts from ThwargLauncher if they exist and none are configured yet.
///   3. Copies the AC install to ACBase\ if it lives under a protected path (Program Files),
///      so HardLinkLauncher can create hard links without elevation.
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

    private static readonly string LegacyThwargAppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ThwargLauncher");

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
        TryImportLegacyThwargFilterData();
    }

    /// <summary>
    /// Returns true if <see cref="PrepareHardLinkBaseAsync"/> would need to perform
    /// a file copy (i.e. the AC install is in a protected path and ACBase doesn't exist yet).
    /// Use this to decide whether to show a progress window before calling PrepareHardLinkBaseAsync.
    /// </summary>
    public bool HardLinkBaseNeedsCopy()
    {
        var clientPath = _config.GameClientPath;
        if (string.IsNullOrWhiteSpace(clientPath)) return false;

        var existing = _config.GetSetting("HardLinkBasePath");
        if (!string.IsNullOrWhiteSpace(existing) && Directory.Exists(existing)) return false;

        var clientDir = Path.GetDirectoryName(clientPath)!;
        return IsProtectedPath(clientDir);
    }

    /// <summary>
    /// Ensures the ACBase directory is ready for <c>HardLinkLauncher</c>.
    /// If the configured client path is under a protected directory (Program Files),
    /// copies the AC install to <c>%LocalAppData%\ShadowLauncher\ACBase\</c> once.
    /// If the path is unprotected, just stores it as-is.
    /// Progress is reported via <paramref name="progress"/>; pass null to run silently.
    /// Returns the resolved base path, or null if the client path is not configured.
    /// </summary>
    public async Task<string?> PrepareHardLinkBaseAsync(
        IProgress<AcBaseCopyProgress>? progress = null,
        CancellationToken ct = default)
    {
        var clientPath = _config.GameClientPath;
        if (string.IsNullOrWhiteSpace(clientPath) || !File.Exists(clientPath))
        {
            _logger.LogWarning("PrepareHardLinkBase: client path not configured or not found");
            return null;
        }

        var clientDir = Path.GetDirectoryName(clientPath)!;

        // Already resolved from a previous run — nothing to do.
        var existing = _config.GetSetting("HardLinkBasePath");
        if (!string.IsNullOrWhiteSpace(existing) && Directory.Exists(existing))
        {
            _logger.LogDebug("ACBase already prepared at {Path}", existing);
            return existing;
        }

        if (!IsProtectedPath(clientDir))
        {
            // Custom install path — hard links work directly, no copy needed.
            _config.SetSetting("HardLinkBasePath", clientDir);
            _config.Save();
            _logger.LogInformation("ACBase: using unprotected install dir directly: {Path}", clientDir);
            return clientDir;
        }

        // Protected path — copy the install to LocalAppData\ShadowLauncher\ACBase\.
        var acBaseDir = Path.Combine(_config.DataDirectory, "ACBase");
        _logger.LogInformation("ACBase: protected install detected, copying to {Dest}", acBaseDir);

        try
        {
            Directory.CreateDirectory(acBaseDir);
            var files = Directory.GetFiles(clientDir);
            for (var i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(files[i]);
                progress?.Report(new AcBaseCopyProgress(i, files.Length, fileName));
                File.Copy(files[i], Path.Combine(acBaseDir, fileName), overwrite: true);
            }
            progress?.Report(new AcBaseCopyProgress(files.Length, files.Length, string.Empty));
        }
        catch (OperationCanceledException)
        {
            // Clean up partial copy so we retry cleanly next time.
            try { Directory.Delete(acBaseDir, recursive: true); } catch { /* best-effort */ }
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ACBase copy failed — cleaning up partial directory");
            try { Directory.Delete(acBaseDir, recursive: true); } catch { /* best-effort */ }
            return null;
        }

        _config.SetSetting("HardLinkBasePath", acBaseDir);
        _config.Save();
        _logger.LogInformation("ACBase copy complete: {Path}", acBaseDir);
        return acBaseDir;
    }

    private static bool IsProtectedPath(string path) =>
        path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase);

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

            var thwargAccountsFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ThwargLauncher", "Accounts.txt");

            if (!File.Exists(thwargAccountsFile))
                return;

            var lines = await File.ReadAllLinesAsync(thwargAccountsFile);
            var imported = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('\''))
                    continue;
                if (trimmed.StartsWith("Version=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var props = AccountLineParser.Parse(trimmed);
                if (!props.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
                    continue;
                props.TryGetValue("Password", out var password);
                props.TryGetValue("Alias", out var alias);
                props.TryGetValue("PreferencePath", out var preferencePath);

                var account = new Core.Models.Account
                {
                    Id = name.ToLowerInvariant(),
                    Name = name,
                    PasswordHash = password ?? string.Empty,
                    Notes = string.IsNullOrWhiteSpace(alias) ? string.Empty : alias,
                    PreferencePath = string.IsNullOrWhiteSpace(preferencePath) ? string.Empty : preferencePath,
                };

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

    private void TryImportLegacyThwargFilterData()
    {
        try
        {
            if (!Directory.Exists(LegacyThwargAppDataFolder))
                return;

            Directory.CreateDirectory(ShadowLauncherPaths.AppFolder);

            CopyDirectoryIfMissing(
                Path.Combine(LegacyThwargAppDataFolder, "LoginCommands"),
                ShadowLauncherPaths.LoginCommandsFolder);

            CopyDirectoryIfMissing(
                Path.Combine(LegacyThwargAppDataFolder, "characters"),
                ShadowLauncherPaths.CharactersFolder);

            var legacyDefaults = Path.Combine(LegacyThwargAppDataFolder, "DefaultCharacters.json");
            if (File.Exists(legacyDefaults) && !File.Exists(ShadowLauncherPaths.DefaultCharactersFile))
                File.Copy(legacyDefaults, ShadowLauncherPaths.DefaultCharactersFile, overwrite: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "First-run: could not import legacy ThwargFilter data");
        }
    }

    private static void CopyDirectoryIfMissing(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
            return;

        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var dest = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(dest))
                File.Copy(file, dest, overwrite: false);
        }
    }
}
