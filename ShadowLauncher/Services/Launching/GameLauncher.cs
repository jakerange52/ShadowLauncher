using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Exceptions;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Infrastructure.Paths;
using ShadowLauncher.Services.Dats;
using ShadowLauncher.Services.LoginCommands;

namespace ShadowLauncher.Services.Launching;

/// <summary>
/// Orchestrates launching acclient.exe for a given account/server combination.
///
/// Launch strategy:
///   All servers — launch via <see cref="DecalInjector.LaunchSuspendedAndInject"/>:
///     acclient is created suspended, Decal's Inject.dll is loaded into it, then the
///     main thread is resumed. Decal handles the single-instance mutex internally,
///     enabling multi-client without any mutex manipulation.
///
///   Custom-DAT servers (DatSetId in DatRegistry.xml) — additionally use the active IInstancePreparer
///     to create a per-instance directory with hard links to the correct DAT files before
///     launching, so each client sees its own working directory and DAT set.
///
///   No Decal — single client only, launched directly via Process.Start.
/// </summary>
public class GameLauncher : IGameLauncher
{
    private readonly IConfigurationProvider _config;
    private readonly ILogger<GameLauncher> _logger;
    private readonly LoginCommandsService _loginCommandsService;
    private readonly IInstancePreparer _instancePreparer;
    private readonly IDatSetService _datSetService;

    public GameLauncher(
        IConfigurationProvider config,
        IInstancePreparer instancePreparer,
        IDatSetService datSetService,
        LoginCommandsService loginCommandsService,
        ILogger<GameLauncher> logger)
    {
        _config = config;
        _instancePreparer = instancePreparer;
        _datSetService = datSetService;
        _loginCommandsService = loginCommandsService;
        _logger = logger;
    }

    /// <summary>Carries the resolved exe path, working directory, and optional instance dir for cleanup watcher.</summary>
    private record LaunchEnvironment(string ExePath, string WorkingDir, string? InstanceDir);

    public async Task<LaunchResult> LaunchGameAsync(Account account, Server server)
    {
        var result = new LaunchResult();

        try
        {
            var clientPath = _config.GameClientPath;
            if (string.IsNullOrWhiteSpace(clientPath) || !File.Exists(clientPath))
            {
                result.ErrorMessage = "Game client path is not configured or file not found.";
                return result;
            }

            var arguments = BuildLaunchArguments(account, server);

            // Resolve Decal's Inject.dll — user config takes priority, then registry auto-detect.
            // If Decal is not found, launch normally (single client only).
            string? decalInjectPath = null;
            string decalMode;
            if (_config.AttemptDecalInjection)
            {
                decalInjectPath = DecalInjector.ResolveDecalInjectPath(_config.DecalPath);
                decalMode = decalInjectPath is not null ? "Decal" : "no Decal";
            }
            else
            {
                decalMode = "Decal disabled";
            }

            // "any" or null means stay at character select instead of auto-logging in.
            var defaultChar = _loginCommandsService.GetDefaultCharacter(account.Name, server.Name);
            var launchCharacter = (string.IsNullOrEmpty(defaultChar) || defaultChar == "any") ? "None" : defaultChar;

            // Write launch files BEFORE starting the process (4-tick window after connect).
            // ThwargFilter path: users who already have ThwargFilter need no ShadowFilter.
            // ShadowFilter path: optional first-party filter; defers char-select if Thwarg is loaded.
            WriteShadowFilterLaunchFile(account.Name, server.Name, launchCharacter);
            WriteThwargFilterLaunchFile(account.Name, server.Name, launchCharacter);

            // ── Resolve which exe/directory to launch from ──────────────────────
            var env = await ResolveInstancePathAsync(server, clientPath, result);
            if (env is null) return result;

            // Swap per-account UserPreferences.ini into the default location (PreferencePath).
            ApplyPreferencePath(account);

            // ── Start the process ───────────────────────────────────────────────
            var processId = LaunchWithDecal(env.ExePath, arguments, env.WorkingDir, decalInjectPath);

            if (processId <= 0)
            {
                if (env.InstanceDir is not null)
                {
                    result.ErrorMessage = "Failed to launch game process from instance directory. Check the log for details.";
                    _logger.LogError(
                        "Instance launch returned PID {Pid} for server '{Server}'. InstanceExeExists={ExeExists}",
                        processId, server.Name,
                        File.Exists(env.ExePath));
                }
                else
                {
                    result.ErrorMessage = "Failed to launch game process.";
                }
                return result;
            }

            // Hand the instance directory to the cleanup watcher.
            if (env.InstanceDir is not null)
            {
                Process? process = null;
                try { process = Process.GetProcessById(processId); } catch { }
                if (process is not null)
                    _ = _instancePreparer.WatchAndCleanupAsync(process, env.InstanceDir);
            }

            // ── Post-launch ─────────────────────────────────────────────────────
            // Intro movies / character select clicks are owned by ShadowFilter when Decal is injected.

            // Rename the window title so instances are identifiable in the taskbar.
            WindowTitleSetter.SetTitleAsync(processId, account.Name, server.Name);

            // Confirm the process is still alive after all launch machinery has finished.
            try
            {
                using var proc = Process.GetProcessById(processId);
                result.Success = !proc.HasExited;
            }
            catch (ArgumentException)
            {
                result.Success = false;
            }

            result.ProcessId = processId;

            if (result.Success)
                _logger.LogInformation(
                    "Launched {Account} on {Server} — PID {Pid} (character: {Character}, {DecalMode})",
                    account.Name, server.Name, processId, launchCharacter, decalMode);
            else
            {
                _logger.LogWarning("Game process PID {Pid} exited immediately after launch ({Account} on {Server})",
                    processId, account.Name, server.Name);
                result.ErrorMessage = "Game process exited immediately after launch.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch game for {Account}", account.Name);
            result.ErrorMessage = ex.Message;
            throw new LaunchException($"Failed to launch game: {ex.Message}", ex);
        }

        return result;
    }

    /// <summary>
    /// Determines which exe and working directory to use for this launch.
    /// Priority:
    ///   1. Custom DAT source (local path or zip URL) — ensures the source is ready, then uses IInstancePreparer.
    ///   2. DatSetId registered in DatRegistry.xml  — validates the set is downloaded, then uses IInstancePreparer.
    ///   3. Neither                                  — returns the configured client path directly.
    /// Returns null and populates <paramref name="result"/> on any failure.
    /// </summary>
    private async Task<LaunchEnvironment?> ResolveInstancePathAsync(Server server, string clientPath, LaunchResult result)
    {
        var datSetId = server.DatSetId;
        var hasCustomSource = !string.IsNullOrWhiteSpace(server.CustomDatRegistryPath)
                           || !string.IsNullOrWhiteSpace(server.CustomDatZipUrl);

        if (hasCustomSource)
        {
            try
            {
                await _datSetService.EnsureCustomDatSourceReadyAsync(server);
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Failed to prepare custom DAT source for '{server.Name}': {ex.Message}";
                _logger.LogError(ex, "EnsureCustomDatSourceReadyAsync failed for '{Server}'", server.Name);
                return null;
            }

            return await PrepareInstanceEnvironmentAsync(server, datSetId, result);
        }

        if (!string.IsNullOrWhiteSpace(datSetId))
        {
            var datSet = await _datSetService.GetDatSetAsync(datSetId);
            if (datSet is null)
            {
                result.ErrorMessage = $"DAT set '{datSetId}' required by server '{server.Name}' was not found in the DAT registry. " +
                    "Check your internet connection or verify the DatRegistry.xml is reachable.";
                _logger.LogError("DAT set '{DatSetId}' not found in registry for server '{Server}'", datSetId, server.Name);
                return null;
            }

            if (!await _datSetService.IsDatSetReadyAsync(datSetId))
            {
                result.ErrorMessage = $"DAT files for '{server.Name}' are not ready. " +
                    $"Expected in: {_datSetService.GetLocalDatSetPath(datSetId)}\n\nOpen the DAT Manager to download them.";
                return null;
            }

            return await PrepareInstanceEnvironmentAsync(server, datSetId, result);
        }

        // No custom DAT source — launch directly from the configured client path.
        return new LaunchEnvironment(clientPath, Path.GetDirectoryName(clientPath) ?? string.Empty, InstanceDir: null);
    }

    private async Task<LaunchEnvironment?> PrepareInstanceEnvironmentAsync(Server server, string? datSetId, LaunchResult result)
    {
        _logger.LogDebug("Server '{Server}' requires DAT set '{DatSetId}' — preparing instance",
            server.Name, datSetId);
        var env = await _instancePreparer.PrepareInstanceAsync(server);
        if (env is null)
        {
            result.ErrorMessage = "Instance preparer failed to prepare the instance directory. Check the log for details.";
            return null;
        }
        return new LaunchEnvironment(env.ExePath, env.WorkingDir, InstanceDir: env.WorkingDir);
    }

    public Task<bool> IsGameProcessRunningAsync(int processId)
    {
        if (processId <= 0)
            return Task.FromResult(false);

        try
        {
            using var process = Process.GetProcessById(processId);
            try
            {
                return Task.FromResult(!process.HasExited);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Process exists but we lack rights to query exit status (Idle/System/elevated).
                // Treat as still running so we don't drop a live journaled session.
                return Task.FromResult(true);
            }
        }
        catch (ArgumentException)
        {
            return Task.FromResult(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Builds the command-line argument string for acclient.exe.
    /// Three variants depending on emulator type and secure-logon flag:
    ///   GDLE        — -h HOST -p PORT -a USER:PASS -rodat on|off
    ///   ACE secure  — -a USER -h HOST -p PORT -glsticketdirect PASS -rodat on|off
    ///   ACE default — -a USER -v PASS -h HOST -p PORT -rodat on|off
    /// </summary>
    private static string BuildLaunchArguments(Account account, Server server)
    {
        var rodat = server.DefaultRodat ? "on" : "off";
        var password = account.PasswordHash; // stored as plaintext for game login

        return server.Emulator switch
        {
            EmulatorType.GDLE =>
                $"-h {server.Hostname} -p {server.Port} -a {account.Name}:{password} -rodat {rodat}",

            EmulatorType.ACE when server.SecureLogon =>
                $"-a {account.Name} -h {server.Hostname} -p {server.Port} -glsticketdirect {password} -rodat {rodat}",

            // ACE without secure logon
            _ => $"-a {account.Name} -v {password} -h {server.Hostname} -p {server.Port} -rodat {rodat}"
        };
    }

    private int LaunchWithDecal(string exePath, string arguments, string workingDir, string? decalInjectPath)
    {
        if (decalInjectPath is not null)
        {
            var processId = DecalInjector.LaunchSuspendedAndInject(exePath, arguments, workingDir, decalInjectPath, out var win32Error);
            if (processId > 0)
                _logger.LogDebug("Launched acclient with Decal injection, PID {Pid}", processId);
            else
                _logger.LogError("CreateProcess failed for '{Exe}' — Win32Error={Error} (0x{ErrorHex}), WorkingDir='{WorkingDir}'",
                    exePath, win32Error, win32Error.ToString("X8"), workingDir);
            return processId;
        }
        else
        {
            // No Decal — single client only, plain launch.
                var process = Process.Start(new ProcessStartInfo
                {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
            });
            if (process is null)
            {
                _logger.LogError("Process.Start returned null for '{Exe}'.", exePath);
                return -1;
            }
            _logger.LogDebug("Launched acclient without Decal, PID {Pid}", process.Id);
            return process.Id;
        }
    }

    /// <summary>
    /// Copies the account's <see cref="Account.PreferencePath"/> ini into the default
    /// Documents\Asheron's Call\UserPreferences.ini location immediately before launch.
    /// acclient reads that file once at startup; stagger multi-launch via MultiLaunchDelaySeconds
    /// to avoid two clients racing on the same default file.
    /// </summary>
    private void ApplyPreferencePath(Account account)
    {
        if (string.IsNullOrWhiteSpace(account.PreferencePath))
            return;

        var sourcePath = account.PreferencePath.Trim();
        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("PreferencePath not found for {Account}: {Path}", account.Name, sourcePath);
            return;
        }

        var defaultPath = ResolveDefaultUserPreferencesPath();
        if (defaultPath is null)
        {
            _logger.LogWarning("Default UserPreferences.ini location not found for {Account}", account.Name);
            return;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var defaultDir = Path.GetDirectoryName(defaultPath);
                if (!string.IsNullOrEmpty(defaultDir))
                    Directory.CreateDirectory(defaultDir);

                File.Copy(sourcePath, defaultPath, overwrite: true);
                _logger.LogDebug("Applied PreferencePath for {Account}: {Source} -> {Dest}",
                    account.Name, sourcePath, defaultPath);
                return;
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                _logger.LogDebug(ex,
                    "PreferencePath copy locked for {Account}, retry {Attempt}/{Max}",
                    account.Name, attempt, maxAttempts);
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to apply PreferencePath for {Account}: {Source}",
                    account.Name, sourcePath);
                return;
            }
        }

        _logger.LogWarning(
            "Failed to apply PreferencePath for {Account} after {Max} attempts: {Source}",
            account.Name, maxAttempts, sourcePath);
    }

    private static string? ResolveDefaultUserPreferencesPath()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var withApostrophe = Path.Combine(documents, "Asheron's Call", "UserPreferences.ini");
        if (File.Exists(withApostrophe))
            return withApostrophe;

        var withoutApostrophe = Path.Combine(documents, "Asherons Call", "UserPreferences.ini");
        if (File.Exists(withoutApostrophe))
            return withoutApostrophe;

        // Prefer the standard folder even if the file does not exist yet.
        var preferredDir = Path.Combine(documents, "Asheron's Call");
        if (Directory.Exists(preferredDir))
            return withApostrophe;

        var altDir = Path.Combine(documents, "Asherons Call");
        if (Directory.Exists(altDir))
            return withoutApostrophe;

        return withApostrophe;
    }

    private void CleanupShadowFilterLaunchFile(string accountName, string serverName)
    {
        try
        {
            var filePath = ShadowLauncherPaths.GetShadowFilterLaunchFilePath(serverName, accountName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Removed ShadowFilter launch file for {Account} on {Server}", accountName, serverName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove ShadowFilter launch file");
        }

        try
        {
            var thwargPath = ShadowLauncherPaths.GetThwargFilterLaunchFilePath(serverName, accountName);
            if (File.Exists(thwargPath))
            {
                File.Delete(thwargPath);
                _logger.LogDebug("Removed ThwargFilter launch file for {Account} on {Server}", accountName, serverName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove ThwargFilter launch file");
        }
    }

    /// <summary>
    /// Removes the launch file only when no other active session shares the same account/server.
    /// Prevents multi-box from losing launch info for a still-running client.
    /// </summary>
    public void CleanupShadowFilterLaunchFileIfUnused(
        string accountName,
        string serverName,
        IEnumerable<GameSession> activeSessions,
        int exceptProcessId)
    {
        var stillActive = activeSessions.Any(s =>
            s.ProcessId != exceptProcessId
            && string.Equals(s.AccountName, accountName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.ServerName, serverName, StringComparison.OrdinalIgnoreCase));

        if (!stillActive)
            CleanupShadowFilterLaunchFile(accountName, serverName);
    }

    /// <summary>
    /// Writes a ShadowFilter launch file so the plugin knows which account/server/character
    /// is logging in. This enables character tracking and login command execution.
    /// </summary>
    private void WriteShadowFilterLaunchFile(string accountName, string serverName, string characterName)
    {
        try
        {
            Directory.CreateDirectory(ShadowLauncherPaths.LaunchFilesFolder);
            var filePath = ShadowLauncherPaths.GetShadowFilterLaunchFilePath(serverName, accountName);
            using var writer = new StreamWriter(filePath, append: false);
            writer.WriteLine("FileVersion:1.2");
            writer.WriteLine($"Timestamp=TimeUtc:'{DateTime.UtcNow:o}'");
            writer.WriteLine($"ServerName:{serverName}");
            writer.WriteLine($"AccountName:{accountName}");
            writer.WriteLine($"CharacterName:{characterName}");

            _logger.LogDebug("Wrote ShadowFilter launch file for {Account} on {Server}", accountName, serverName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write ShadowFilter launch file");
        }
    }

    /// <summary>
    /// Dual-writes a ThwargFilter launch file so ThwargFilter can auto-login when loaded
    /// alongside ShadowFilter. Same key/value format ThwargLauncher uses.
    /// </summary>
    private void WriteThwargFilterLaunchFile(string accountName, string serverName, string characterName)
    {
        try
        {
            Directory.CreateDirectory(ShadowLauncherPaths.ThwargLaunchFilesFolder);
            var filePath = ShadowLauncherPaths.GetThwargFilterLaunchFilePath(serverName, accountName);
            using var writer = new StreamWriter(filePath, append: false);
            writer.WriteLine("FileVersion:1.2");
            writer.WriteLine($"Timestamp=TimeUtc:'{DateTime.UtcNow:o}'");
            writer.WriteLine($"ServerName:{serverName}");
            writer.WriteLine($"AccountName:{accountName}");
            writer.WriteLine($"CharacterName:{characterName}");

            _logger.LogDebug("Wrote ThwargFilter launch file for {Account} on {Server}", accountName, serverName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write ThwargFilter launch file");
        }
    }
}
