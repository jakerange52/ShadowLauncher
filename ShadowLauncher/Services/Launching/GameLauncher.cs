using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Exceptions;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Native;
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
///   Custom-DAT servers (DatSetId in DatRegistry.xml) — additionally use SymlinkLauncher
///     to create a per-instance directory with symlinks to the correct DAT files before
///     launching, so each client sees its own working directory and DAT set.
///
///   No Decal — single client only, launched directly via Process.Start.
/// </summary>
public class GameLauncher : IGameLauncher
{
    private readonly IConfigurationProvider _config;
    private readonly IEventAggregator _events;
    private readonly ILogger<GameLauncher> _logger;
    private readonly LoginCommandsService _loginCommandsService;
    private readonly SymlinkLauncher _symlinkLauncher;
    private readonly IDatSetService _datSetService;

    public GameLauncher(
        IConfigurationProvider config,
        IEventAggregator events,
        SymlinkLauncher symlinkLauncher,
        IDatSetService datSetService,
        LoginCommandsService loginCommandsService,
        ILogger<GameLauncher> logger)
    {
        _config = config;
        _events = events;
        _symlinkLauncher = symlinkLauncher;
        _datSetService = datSetService;
        _loginCommandsService = loginCommandsService;
        _logger = logger;
    }

    public async Task<LaunchResult> LaunchGameAsync(Account account, Character character, Server server)
    {
        var result = new LaunchResult { StartTime = DateTime.UtcNow };

        try
        {
            var clientPath = _config.GameClientPath;
            if (string.IsNullOrWhiteSpace(clientPath) || !File.Exists(clientPath))
            {
                result.ErrorMessage = "Game client path is not configured or file not found.";
                return result;
            }

            // Build command-line args for this account/server combo.
            // Format varies by emulator type and secure-logon flag; see BuildLaunchArguments.
            var arguments = BuildLaunchArguments(account, server);

            // Resolve Decal's Inject.dll — user config takes priority, then registry auto-detect.
            // If Decal is not found, launch normally (single client only).
            var decalInjectPath = DecalInjector.ResolveDecalInjectPath(_config.DecalPath);
            if (decalInjectPath is not null)
                _logger.LogInformation("Decal injection: {Path}", decalInjectPath);
            else
                _logger.LogInformation("Decal not found — single client launch");

            // Resolve the saved default character
            // "any" or null means stay at character select instead of auto-logging in.
            var defaultChar = _loginCommandsService.GetDefaultCharacter(account.Name, server.Name);
            var launchCharacter = (string.IsNullOrEmpty(defaultChar) || defaultChar == "any")
                ? "None"
                : defaultChar;

            _logger.LogInformation("Launching game for {Account} on {Server} (character: {Character})",
                account.Name, server.Name, launchCharacter);

            // Write ThwargFilter launch file BEFORE starting the process (same as ThwargLauncher).
            // ThwargFilter's timer starts on first server connect and only runs 4 ticks (states 0-3)
            // before stopping permanently. The file must exist by then.
            WriteThwargFilterLaunchFile(account.Name, server.Name, launchCharacter);

            int processId;

            // ── Path selection ─────────────────────────────────────────────────────
            // Priority:
            //   1. Custom DAT source (local path or zip URL) — Dat Developer Mode.
            //      EnsureCustomDatSourceReadyAsync downloads the zip if needed, then
            //      SymlinkLauncher uses whatever local directory it resolved to.
            //   2. DatSetId registered in DatRegistry.xml — community server with custom DATs.
            //   3. Neither — launch directly from the configured client path.
            var datSetId = server.DatSetId;
            bool useSymlink = false;

            bool hasCustomSource = !string.IsNullOrWhiteSpace(server.CustomDatRegistryPath)
                                || !string.IsNullOrWhiteSpace(server.CustomDatZipUrl);

            if (hasCustomSource)
            {
                if (!SymlinkLauncher.CanCreateSymlinks())
                {
                    result.ErrorMessage = "Symbolic link creation failed. Sign out and back in to activate the privilege granted during install, then try again.";
                    _logger.LogError("CanCreateSymlinks() returned false for server '{Server}'", server.Name);
                    return result;
                }

                try
                {
                    await _datSetService.EnsureCustomDatSourceReadyAsync(server);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = $"Failed to prepare custom DAT source for '{server.Name}': {ex.Message}";
                    _logger.LogError(ex, "EnsureCustomDatSourceReadyAsync failed for '{Server}'", server.Name);
                    return result;
                }

                useSymlink = true;
            }
            else if (!string.IsNullOrWhiteSpace(datSetId))
            {
                var datSet = await _datSetService.GetDatSetAsync(datSetId);
                if (datSet is null)
                {
                    result.ErrorMessage = $"DAT set '{datSetId}' required by server '{server.Name}' was not found in the DAT registry. " +
                        "Check your internet connection or verify the DatRegistry.xml is reachable.";
                    _logger.LogError("DAT set '{DatSetId}' not found in registry for server '{Server}'", datSetId, server.Name);
                    return result;
                }

                if (!SymlinkLauncher.CanCreateSymlinks())
                {
                    result.ErrorMessage = "Symbolic link creation failed. Sign out and back in to activate the privilege granted during install, then try again.";
                    _logger.LogError("CanCreateSymlinks() returned false for server '{Server}'", server.Name);
                    return result;
                }

                useSymlink = true;
            }

            if (useSymlink)
            {
                // Custom-source servers (local path or zip URL) were already verified by
                // EnsureCustomDatSourceReadyAsync above — skip the registry-based readiness check.
                // For registry servers, confirm the DAT set files are fully downloaded first.
                if (!hasCustomSource && !await _datSetService.IsDatSetReadyAsync(datSetId!))
                {
                    result.ErrorMessage = $"DAT files for '{server.Name}' are not ready. " +
                        $"Expected in: {_datSetService.GetLocalDatSetPath(datSetId!)}\n\nOpen the DAT Manager to download them.";
                    return result;
                }
                _logger.LogInformation(
                    "Server '{Server}' requires DAT set '{DatSetId}' — using SymlinkLauncher",
                    server.Name, datSetId);

                var instanceDir = await _symlinkLauncher.PrepareInstanceAsync(server);
                if (instanceDir is null)
                {
                    result.ErrorMessage = "SymlinkLauncher failed to prepare the instance directory. Check the log for details.";
                    return result;
                }

                var instanceExe = Path.Combine(instanceDir, "acclient.exe");

                System.Diagnostics.Process? process = null;
                processId = LaunchWithDecal(instanceExe, arguments, instanceDir, decalInjectPath);
                if (processId > 0)
                    try { process = System.Diagnostics.Process.GetProcessById(processId); } catch { }

                if (processId <= 0 || process is null)
                {
                    result.ErrorMessage = "Failed to launch game process from symlink instance. Check the log for details.";
                    _logger.LogError("Symlink launch returned PID {Pid} for server '{Server}'", processId, server.Name);
                    return result;
                }

                _ = _symlinkLauncher.WatchAndCleanupAsync(process, instanceDir);
            }
            else
            {
                processId = LaunchWithDecal(clientPath, arguments, Path.GetDirectoryName(clientPath) ?? string.Empty, decalInjectPath);
                if (processId <= 0)
                {
                    result.ErrorMessage = "Failed to launch game process.";
                    return result;
                }
            }

            // Send click events to the game window to dismiss intro movie screens.
            // Only triggered when a specific character was chosen (not "stay at select").
            if (launchCharacter != "None")
            {
                MovieSkipper.StartSkipping(processId);
            }

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
            {
                _events.Publish(new GameLaunchedEvent(account.Id, character.Name, server.Id, processId));
                _logger.LogInformation("Game launched successfully, PID: {Pid}", processId);
            }
            else
            {
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

    public Task TerminateGameAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            _logger.LogInformation("Terminated game process {Pid}", processId);
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
        return Task.CompletedTask;
    }

    public Task<bool> IsGameProcessRunningAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return Task.FromResult(!process.HasExited);
        }
        catch (ArgumentException)
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
            var processId = DecalInjector.LaunchSuspendedAndInject(exePath, arguments, workingDir, decalInjectPath);
            if (processId > 0)
                _logger.LogInformation("Launched acclient with Decal injection, PID {Pid}", processId);
            return processId;
        }
        else
        {
            // No Decal — single client only, plain launch.
            var process = System.Diagnostics.Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                UseShellExecute = false,
            });
            if (process is null) return -1;
            _logger.LogInformation("Launched acclient without Decal, PID {Pid}", process.Id);
            return process.Id;
        }
    }

    /// <summary>
    /// Removes the ThwargFilter launch file written at session start.
    /// Called when the game process exits so stale launch files do not accumulate.
    /// </summary>
    public void CleanupThwargFilterLaunchFile(string accountName, string serverName)
    {
        try
        {
            var launchFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ThwargLauncher", "LaunchFiles");
            var filePath = Path.Combine(launchFolder, $"launch_ThwargFilter_{serverName}_{accountName}.txt");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Removed ThwargFilter launch file for {Account} on {Server}", accountName, serverName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove ThwargFilter launch file");
        }
    }

    /// <summary>
    /// Writes a ThwargFilter launch file so the filter knows which account/server/character
    /// is logging in. This enables ThwargFilter to record character lists and execute login commands.
    /// File: %AppData%\ThwargLauncher\LaunchFiles\launch_ThwargFilter_{Server}_{Account}.txt
    /// </summary>
    private void WriteThwargFilterLaunchFile(string accountName, string serverName, string characterName)
    {
        try
        {
            var launchFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ThwargLauncher", "LaunchFiles");
            Directory.CreateDirectory(launchFolder);

            var filePath = Path.Combine(launchFolder, $"launch_ThwargFilter_{serverName}_{accountName}.txt");
            using var writer = new StreamWriter(filePath, append: false);
            writer.WriteLine("FileVersion:1.2");
            writer.WriteLine($"Timestamp=TimeUtc:'{DateTime.UtcNow:o}'");
            writer.WriteLine($"ServerName:{serverName}");
            writer.WriteLine($"AccountName:{accountName}");
            writer.WriteLine($"CharacterName:{characterName}");

            _logger.LogInformation("Wrote ThwargFilter launch file for {Account} on {Server}", accountName, serverName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write ThwargFilter launch file");
        }
    }
}

public record GameLaunchedEvent(string AccountId, string CharacterName, string ServerId, int ProcessId);
