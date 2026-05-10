using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Creates a per-instance "virtual install" directory using symbolic links and directory
/// junctions, then launches acclient.exe from that directory.
///
/// Why this approach:
///   AC's acclient.exe uses its working directory to locate DAT files, so each instance
///   launched from its own junction directory gets an independent working-directory
///   context. Critically, because every instance has its own directory path, the OS
///   treats their mutex names as distinct — each instance is fully independent.
///
/// Directory layout created per launch:
///   %LocalAppData%\ShadowLauncher\Instances\{guid}\
///       acclient.exe          → file symlink  → real acclient.exe (or DAT set override)
///       client_portal.dat     → file symlink  → DatSet cache or retail client dir
///       client_cell_1.dat     → file symlink  → ...
///       client_local_English.dat → file symlink  → ...
///       client_highres.dat    → file symlink  → ... (if present)
///
/// Cleanup:
///   The instance directory is deleted after the game process exits (or on next
///   launcher startup via <see cref="CleanupStaleInstances"/>).
///
/// Prerequisites:
///   Creating file symlinks on Windows requires either:
///     (a) Developer Mode enabled (no elevation needed), or
///     (b) SeCreateSymbolicLinkPrivilege (Administrator / elevated process).
///   The launcher should check <see cref="CanCreateSymlinks"/> before using this path
///   If symlinks are unavailable, ShadowLauncher will report the error and ask the user to enable Developer Mode.
/// </summary>
public class SymlinkLauncher
{
    // Windows SDK symbolic link flag values (from winbase.h)
    private const int SymlinkFlagFile = 0;
    private const int SymlinkFlagAllowUnprivilegedCreate = 2;

    // DAT filenames acclient.exe looks for in its working directory.
    internal static readonly string[] KnownDatFiles =
    [
        "client_portal.dat",
        "client_cell_1.dat",
        "client_local_English.dat",
        "client_highres.dat",
    ];

    private readonly IConfigurationProvider _config;
    private readonly IDatSetService _datSetService;
    private readonly ILogger<SymlinkLauncher> _logger;

    public SymlinkLauncher(
        IConfigurationProvider config,
        IDatSetService datSetService,
        ILogger<SymlinkLauncher> logger)
    {
        _config = config;
        _datSetService = datSetService;
        _logger = logger;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the current process can create symbolic links.
    /// Call this before deciding whether to use SymlinkLauncher or the legacy path.
    /// </summary>
    public static bool CanCreateSymlinks()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"sl_symtest_{Guid.NewGuid():N}");
        var linkPath = Path.Combine(testDir, "test.lnk");
        try
        {
            Directory.CreateDirectory(testDir);
            // Try Developer Mode first, then privilege-based creation.
            if (CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagAllowUnprivilegedCreate))
                return true;
            return CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagFile);
        }
        catch
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(testDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Returns a multi-line diagnostic string describing exactly why symlink creation
    /// is working or failing in the current session. Intended for log output when
    /// <see cref="CanCreateSymlinks"/> returns false so support has full context.
    /// </summary>
    public static string DiagnoseSymlinkCapability()
    {
        var lines = new System.Text.StringBuilder();

        // ── Who is running ────────────────────────────────────────────────────
        try
        {
            lines.AppendLine($"  User            : {Environment.UserDomainName}\\{Environment.UserName}");
        }
        catch { lines.AppendLine("  User            : (unavailable)"); }

        // ── Is the process elevated? ──────────────────────────────────────────
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            lines.AppendLine($"  Elevated        : {isAdmin}");
        }
        catch { lines.AppendLine("  Elevated        : (unavailable)"); }

        // ── Developer Mode path ───────────────────────────────────────────────
        var testDir = Path.Combine(Path.GetTempPath(), $"sl_diagtest_{Guid.NewGuid():N}");
        var linkPath = Path.Combine(testDir, "test.lnk");
        try
        {
            Directory.CreateDirectory(testDir);

            bool devMode = CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagAllowUnprivilegedCreate);
            int devErr = Marshal.GetLastWin32Error();
            lines.AppendLine($"  DevMode symlink : {(devMode ? "OK" : $"FAILED (Win32={devErr} / 0x{devErr:X8})")}");
            if (File.Exists(linkPath)) File.Delete(linkPath);

            bool privMode = CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagFile);
            int privErr = Marshal.GetLastWin32Error();
            lines.AppendLine($"  Priv symlink    : {(privMode ? "OK" : $"FAILED (Win32={privErr} / 0x{privErr:X8})")}");

            // Win32 error 1314 = ERROR_PRIVILEGE_NOT_HELD — the most common case.
            // This means the privilege was granted (policy) but the current logon
            // session token does not yet contain it. A true sign-out (not lock/unlock)
            // and sign-back-in is required.
            if (!devMode && !privMode && privErr == 1314)
                lines.AppendLine("  Diagnosis       : ERROR_PRIVILEGE_NOT_HELD (1314) — privilege was granted but this " +
                                  "session's token predates it. A full sign-out (Start → your name → Sign out) and " +
                                  "sign-back-in is required. Locking the screen does NOT create a new token.");
            else if (!devMode && !privMode)
                lines.AppendLine($"  Diagnosis       : Unexpected failure — Win32={privErr}. May need to run as administrator once.");
        }
        catch (Exception ex)
        {
            lines.AppendLine($"  Diagnosis       : Exception during test — {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(testDir, recursive: true); } catch { /* best-effort */ }
        }

        return lines.ToString().TrimEnd();
    }

    /// <summary>
    /// Ensures the required DAT set is fully downloaded and creates the per-instance
    /// directory with symlinks. Returns the instance directory path ready for launch,
    /// or null on failure. The caller launches the process from this directory via
    /// <see cref="DecalInjector.LaunchSuspendedAndInject"/>.
    /// Call <see cref="WatchAndCleanupAsync"/> after starting the process.
    /// </summary>
    public async Task<string?> PrepareInstanceAsync(
        Server server,
        IProgress<DatDownloadProgress>? downloadProgress = null)
    {
        var clientPath = _config.GameClientPath;
        var clientDir = Path.GetDirectoryName(clientPath)!;

        // Determine which DAT source directory to use.
        //   - "retail" / null / empty → use the files already next to acclient.exe
        //   - custom local path       → server.CustomDatRegistryPath (Dat Developer Mode)
        //   - custom zip URL          → cached under DatSets\{sanitised server name}
        //   - registry DatSetId       → cached under DatSets\{datSetId}
        var datSetId = server.DatSetId;
        string datSourceDir;

        if (string.IsNullOrWhiteSpace(datSetId) ||
            string.Equals(datSetId, "retail", StringComparison.OrdinalIgnoreCase))
        {
            // No custom source either — use the retail install directory.
            bool hasCustomSource = !string.IsNullOrWhiteSpace(server.CustomDatRegistryPath)
                                || !string.IsNullOrWhiteSpace(server.CustomDatZipUrl);
            if (!hasCustomSource)
            {
                datSourceDir = clientDir;
                _logger.LogInformation("Server '{Server}' uses retail DATs", server.Name);
            }
            else
            {
                datSourceDir = _datSetService.GetLocalDatSetPathForServer(server);
                _logger.LogInformation("Server '{Server}' uses custom DAT source: {Dir}", server.Name, datSourceDir);
            }
        }
        else
        {
            // Registry or custom-override — GetLocalDatSetPathForServer resolves the right path.
            datSourceDir = _datSetService.GetLocalDatSetPathForServer(server);
            _logger.LogInformation("Server '{Server}' requires DAT set '{DatSetId}', source: {Dir}",
                server.Name, datSetId, datSourceDir);
        }

        // Create a fresh instance directory for this launch.
        var instanceId = Guid.NewGuid().ToString("N");
        var instanceDir = Path.Combine(
            _config.DataDirectory, "Instances", instanceId);
        Directory.CreateDirectory(instanceDir);

        // If the server has its own DAT cache dir (not the retail dir), ensure every
        // known DAT file is present there before symlinking. Any that were not included
        // in the download (or partial registry set) are copied from retail, making the
        // cache self-contained so instances never fall back silently to shared retail files.
        if (!string.Equals(datSourceDir, clientDir, StringComparison.OrdinalIgnoreCase))
        {
            await _datSetService.CompleteDatCacheFromRetailAsync(datSourceDir, clientDir);
        }

        _logger.LogInformation("Creating instance directory: {Dir}", instanceDir);

        try
        {
            // Step 1: symlink every file from the retail install directory so the instance
            // looks like a complete AC install (DLLs, configs, etc. all present).
            foreach (var file in Directory.GetFiles(clientDir))
            {
                var fileName = Path.GetFileName(file);
                CreateFileSymlink(Path.Combine(instanceDir, fileName), file);
            }

            // Step 2: override DAT symlinks with files from the DAT set cache.
            // The cache is guaranteed complete (CompleteDatCacheFromRetailAsync ran above),
            // so every known DAT should exist in datSourceDir for non-retail servers.
            foreach (var datFile in KnownDatFiles)
            {
                var sourceDat = Path.Combine(datSourceDir, datFile);
                if (!File.Exists(sourceDat))
                {
                    _logger.LogWarning("DAT '{File}' unexpectedly missing from source '{Dir}' — instance will use retail symlink", datFile, datSourceDir);
                    continue;
                }

                var linkPath = Path.Combine(instanceDir, datFile);
                if (File.Exists(linkPath))
                    File.Delete(linkPath);

                CreateFileSymlink(linkPath, sourceDat);
            }

            // Step 3: override acclient.exe with a custom one from the DAT set cache if present.
            var customClient = Path.Combine(datSourceDir, "acclient.exe");
            if (File.Exists(customClient))
            {
                var exeLink = Path.Combine(instanceDir, "acclient.exe");
                if (File.Exists(exeLink))
                    File.Delete(exeLink);
                CreateFileSymlink(exeLink, customClient);
                _logger.LogInformation("Using custom acclient.exe from DAT set: {Path}", customClient);
            }
            else
            {
                _logger.LogInformation("Using retail acclient.exe: {Path}", clientPath);
            }

            return instanceDir;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare instance {Id}", instanceId);
            CleanupInstance(instanceDir);
            return null;
        }
    }

    /// <summary>
    /// Watches the given process and deletes the instance directory when it exits.
    /// Takes ownership of <paramref name="process"/> and disposes it on completion.
    /// </summary>
    public async Task WatchAndCleanupAsync(System.Diagnostics.Process process, string instanceDir)
    {
        using (process)
        {
            try
            {
                await process.WaitForExitAsync();
            }
            catch { /* process already gone */ }
        }

        await Task.Delay(2000);
        CleanupInstance(instanceDir);
    }

    /// <summary>
    /// Deletes any instance directories left over from a previous session whose
    /// acclient.exe process is no longer running. Call on launcher startup.
    /// </summary>
    public void CleanupStaleInstances()
    {
        var instancesRoot = Path.Combine(_config.DataDirectory, "Instances");
        if (!Directory.Exists(instancesRoot)) return;

        foreach (var dir in Directory.GetDirectories(instancesRoot))
        {
            if (IsInstanceStale(dir))
            {
                CleanupInstance(dir);
                _logger.LogInformation("Cleaned up stale instance: {Dir}", dir);
            }
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private static bool IsInstanceStale(string instanceDir)
    {
        // An instance is stale only if no acclient.exe process is running from it.
        // We check the process's main module path against the instance directory.
        // If we can't read a process's module (access denied / 32-bit vs 64-bit), we
        // conservatively treat it as still active so we never kill a live session.
        // The age fallback is intentionally removed — a 1-hour heuristic was causing
        // live instances to be cleaned up mid-session on long play sessions.
        var instanceDirNorm = Path.GetFullPath(instanceDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("acclient"))
        {
            try
            {
                var exePath = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return false; // can't confirm — keep it

                var exeDirNorm = Path.GetFullPath(Path.GetDirectoryName(exePath)!)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();

                if (exeDirNorm == instanceDirNorm)
                    return false; // actively in use
            }
            catch
            {
                // Can't read the module (e.g. access denied on a 32-bit process from 64-bit host).
                // Conservatively assume it might be ours.
                return false;
            }
            finally
            {
                proc.Dispose();
            }
        }

        return true; // no running acclient.exe found in this instance dir
    }

    private void CleanupInstance(string instanceDir)
    {
        try
        {
            if (Directory.Exists(instanceDir))
                Directory.Delete(instanceDir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up instance directory: {Dir}", instanceDir);
        }
    }

    private static void CreateFileSymlink(string linkPath, string targetPath)
    {
        // Try Developer Mode first, then privilege-based creation.
        if (CreateSymbolicLink(linkPath, targetPath, SymlinkFlagAllowUnprivilegedCreate))
            return;
        if (CreateSymbolicLink(linkPath, targetPath, SymlinkFlagFile))
            return;

        var error = Marshal.GetLastWin32Error();
        throw new InvalidOperationException(
            $"CreateSymbolicLink failed for '{linkPath}' → '{targetPath}' (Win32 error {error}).");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateSymbolicLink(
        string lpSymlinkFileName,
        string lpTargetFileName,
        int dwFlags);
}
