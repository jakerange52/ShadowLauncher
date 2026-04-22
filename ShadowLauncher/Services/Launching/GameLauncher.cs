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
/// Launch strategy selection:
///   Retail servers (no DatSetId)  — legacy path unchanged:
///       Path A: injector.dll + Decal (bypasses mutex via injection)
///       Path B: direct launch + MutexKiller (races to close the mutex handle)
///
///   Custom-DAT servers (DatSetId present in DatRegistry.xml) — symlink path:
///       SymlinkLauncher creates a per-instance directory containing symlinks to
///       acclient.exe and the required DAT files, then launches from there.
///       Each instance has a unique working directory, so AC's mutex is independent
///       per instance — no mutex killing or DLL injection required.
/// </summary>
public class GameLauncher : IGameLauncher
{
    private readonly IConfigurationProvider _config;
    private readonly IEventAggregator _events;
    private readonly ILogger<GameLauncher> _logger;
    private readonly LoginCommandsService _loginCommandsService = new();
    private readonly SymlinkLauncher _symlinkLauncher;
    private readonly IDatSetService _datSetService;

    public GameLauncher(
        IConfigurationProvider config,
        IEventAggregator events,
        SymlinkLauncher symlinkLauncher,
        IDatSetService datSetService,
        ILogger<GameLauncher> logger)
    {
        _config = config;
        _events = events;
        _symlinkLauncher = symlinkLauncher;
        _datSetService = datSetService;
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
            var injectorPath = Path.Combine(AppContext.BaseDirectory, "injector.dll");

            // Resolve the saved default character for this account/server combo.
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
            // If the server declares a DatSetId AND that set is registered in the
            // remote DatRegistry.xml, use SymlinkLauncher (custom DATs, no mutex work).
            // Otherwise fall through to the legacy injector/mutex-kill paths which
            // are unchanged for all normal retail servers.
            var datSetId = server.DatSetId;
            bool useSymlink = false;

            if (!string.IsNullOrWhiteSpace(datSetId))
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
                    result.ErrorMessage = "This server requires DAT file switching via symbolic links, but symlink creation failed. " +
                        "Enable Developer Mode in Windows Settings → For developers, then restart the launcher.";
                    _logger.LogError("CanCreateSymlinks() returned false — Developer Mode may be off");
                    return result;
                }

                useSymlink = true;
            }

            if (useSymlink)
            {
                if (!await _datSetService.IsDatSetReadyAsync(datSetId!))
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
                var decalPath = _config.DecalPath;
                var useInjector = File.Exists(injectorPath)
                    && !string.IsNullOrWhiteSpace(decalPath)
                    && File.Exists(decalPath);

                System.Diagnostics.Process? process = null;
                processId = -1;

                if (useInjector)
                {
                    _logger.LogInformation("Symlink instance: using injector.dll with Decal");
                    try
                    {
                        processId = InjectedLauncher.Launch(instanceExe, arguments, decalPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "injector.dll failed on symlink instance, falling back to direct launch");
                        processId = -1;
                    }

                    if (processId > 0)
                    {
                        await Task.Delay(1000);
                        try { process = System.Diagnostics.Process.GetProcessById(processId); } catch { }
                    }
                }

                if (processId <= 0)
                {
                    // Direct launch + mutex kill
                    _logger.LogInformation("Symlink instance: direct launch + mutex kill");
                    processId = await FallbackLaunchInstanceAsync(instanceExe, arguments, instanceDir);
                    if (processId > 0)
                        try { process = System.Diagnostics.Process.GetProcessById(processId); } catch { }
                }

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
                // ── Legacy retail path (unchanged) ─────────────────────────────────
                var decalPath = _config.DecalPath;
                var useInjector = File.Exists(injectorPath)
                    && !string.IsNullOrWhiteSpace(decalPath)
                    && File.Exists(decalPath);

                if (useInjector)
                {
                    // Path A: injector.dll + Decal — the injector creates the process itself so
                    // the mutex is never contested before Decal intercepts initialisation.
                    _logger.LogInformation("Using injector.dll launch with Decal: {Decal}", decalPath);

                    try
                    {
                        processId = InjectedLauncher.Launch(clientPath, arguments, decalPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "injector.dll call failed, falling back to direct launch");
                        processId = -1;
                    }

                    if (processId <= 0)
                    {
                        // Injector returned an invalid PID — fall back to direct launch + mutex kill.
                        _logger.LogWarning("Injected launch returned PID {Pid}, falling back to direct launch", processId);
                        processId = await FallbackLaunchAsync(clientPath, arguments);
                        if (processId <= 0)
                        {
                            result.ErrorMessage = "Failed to launch game process.";
                            return result;
                        }
                    }
                    else
                    {
                        // Give the injected process a moment to initialise Decal before continuing.
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    // Path B: No Decal / no injector — start acclient normally, then race to
                    // close its mutex handle so a second instance can start.
                    if (!File.Exists(injectorPath))
                        _logger.LogInformation("injector.dll not found, using direct launch + mutex kill");
                    else
                        _logger.LogInformation("No Decal path configured, using direct launch + mutex kill");

                    processId = await FallbackLaunchAsync(clientPath, arguments);
                    if (processId <= 0)
                    {
                        result.ErrorMessage = "Failed to launch game process.";
                        return result;
                    }
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
                var proc = Process.GetProcessById(processId);
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
            var process = Process.GetProcessById(processId);
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
            var process = Process.GetProcessById(processId);
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

    /// <summary>
    /// Launches acclient.exe directly via Process.Start and attempts to close its mutex
    /// so that further instances can start. The mutex is polled up to 5 times with 1-second
    /// delays because acclient creates it asynchronously during startup.
    /// </summary>
    private async Task<int> FallbackLaunchAsync(string clientPath, string arguments)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = clientPath,
            Arguments = arguments,
            UseShellExecute = false
        });

        if (process is null)
            return -1;

        for (int attempt = 0; attempt < 5 && !process.HasExited; attempt++)
        {
            await Task.Delay(1000);
            if (MutexKiller.CloseMutex(process.Id))
            {
                _logger.LogInformation("Closed acclient mutex for PID {Pid} (attempt {Attempt})",
                    process.Id, attempt + 1);
                break;
            }
        }

        return process.HasExited ? -1 : process.Id;
    }

    /// <summary>
    /// Same as <see cref="FallbackLaunchAsync"/> but launches from a symlink instance
    /// directory so the working directory contains the correct DATs.
    /// </summary>
    private async Task<int> FallbackLaunchInstanceAsync(string instanceExePath, string arguments, string instanceDir)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = instanceExePath,
            Arguments = arguments,
            WorkingDirectory = instanceDir,
            UseShellExecute = false
        });

        if (process is null)
            return -1;

        for (int attempt = 0; attempt < 5 && !process.HasExited; attempt++)
        {
            await Task.Delay(1000);
            if (MutexKiller.CloseMutex(process.Id))
            {
                _logger.LogInformation("Closed acclient mutex for symlink instance PID {Pid} (attempt {Attempt})",
                    process.Id, attempt + 1);
                break;
            }
        }

        return process.HasExited ? -1 : process.Id;
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
