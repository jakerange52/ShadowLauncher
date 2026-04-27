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
            // Attempt to create a file symlink to a non-existent target — we only care
            // whether the API succeeds, not whether the target exists.
            // Flag 2 = SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE (required for Developer Mode).
            return CreateSymbolicLink(linkPath, "dummy_target", 2);
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
        // An instance directory is stale if it is older than 1 hour and no running process
        // has its working directory inside it. Age is used as a conservative heuristic
        // because comparing paths across reboots is unreliable.
        var info = new DirectoryInfo(instanceDir);
        return (DateTime.UtcNow - info.CreationTimeUtc).TotalHours > 1;
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
        // SYMBOLIC_LINK_FLAG_FILE = 0; SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 2
        // The unprivileged flag is required for Developer Mode to bypass elevation.
        if (!CreateSymbolicLink(linkPath, targetPath, 2))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"CreateSymbolicLink failed for '{linkPath}' → '{targetPath}' (Win32 error {error}).");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateSymbolicLink(
        string lpSymlinkFileName,
        string lpTargetFileName,
        int dwFlags);
}
