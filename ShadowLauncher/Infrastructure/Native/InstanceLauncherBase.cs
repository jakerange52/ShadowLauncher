using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Shared base for <see cref="HardLinkLauncher"/> (and the dormant <c>SymlinkLauncher</c>).
/// Provides common instance-directory management: cleanup, stale-instance detection,
/// and the list of known DAT filenames acclient.exe looks for at runtime.
/// </summary>
public abstract class InstanceLauncherBase : IInstancePreparer
{
    // DAT filenames acclient.exe looks for in its working directory.
    public static readonly string[] KnownDatFiles =
    [
        "client_portal.dat",
        "client_cell_1.dat",
        "client_local_English.dat",
        "client_highres.dat",
    ];

    protected readonly IConfigurationProvider _config;
    protected readonly IDatSetService _datSetService;
    protected readonly ILogger _logger;

    protected InstanceLauncherBase(
        IConfigurationProvider config,
        IDatSetService datSetService,
        ILogger logger)
    {
        _config = config;
        _datSetService = datSetService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public abstract Task<InstanceEnvironment?> PrepareInstanceAsync(Server server);

    /// <summary>
    /// Resolves the DAT source directory for a server (retail client dir, custom cache, or registry set).
    /// </summary>
    protected string ResolveDataSourceDir(Server server, string clientDir)
    {
        var datSetId = server.DatSetId;
        bool hasCustomSource = !string.IsNullOrWhiteSpace(server.CustomDatRegistryPath)
                            || !string.IsNullOrWhiteSpace(server.CustomDatZipUrl);

        if (string.IsNullOrWhiteSpace(datSetId) ||
            string.Equals(datSetId, "retail", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasCustomSource)
            {
                _logger.LogDebug("Server '{Server}' uses retail DATs", server.Name);
                return clientDir;
            }

            var dir = _datSetService.GetLocalDatSetPathForServer(server);
            _logger.LogDebug("Server '{Server}' uses custom DAT source: {Dir}", server.Name, dir);
            return dir;
        }

        var setDir = _datSetService.GetLocalDatSetPathForServer(server);
        _logger.LogDebug("Server '{Server}' requires DAT set '{DatSetId}', source: {Dir}",
            server.Name, datSetId, setDir);
        return setDir;
    }

    /// <inheritdoc/>
    public async Task WatchAndCleanupAsync(System.Diagnostics.Process process, string instanceDir)
    {
        using (process)
        {
            try { await process.WaitForExitAsync(); }
            catch { /* process already gone */ }
        }

        await Task.Delay(2000);
        CleanupInstance(instanceDir);
    }

    /// <inheritdoc/>
    public void CleanupStaleInstances()
    {
        var instancesRoot = Path.Combine(_config.DataDirectory, "Instances");
        if (!Directory.Exists(instancesRoot)) return;

        foreach (var dir in Directory.GetDirectories(instancesRoot))
        {
            if (!IsInstanceStale(dir))
                continue;

            if (TryCleanupInstance(dir))
                _logger.LogInformation("Cleaned up stale instance: {Dir}", dir);
        }
    }

    // ── Protected helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a fresh, empty instance directory and returns its path.</summary>
    protected string CreateInstanceDirectory()
    {
        var instanceDir = Path.Combine(_config.DataDirectory, "Instances", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(instanceDir);
        return instanceDir;
    }

    protected void CleanupInstance(string instanceDir) => TryCleanupInstance(instanceDir);

    /// <returns>True when the instance directory was fully removed.</returns>
    protected bool TryCleanupInstance(string instanceDir)
    {
        if (!Directory.Exists(instanceDir)) return true;

        // Hard links share an inode with the source file. If any other acclient process is
        // running from the base dir, it holds a lock on shared DLL inodes and prevents deletion
        // of the corresponding hard links here — even though this instance's process has exited.
        // We delete what we can file-by-file and leave the rest for the next startup sweep.
        int deleted = 0, skipped = 0;
        foreach (var file in Directory.GetFiles(instanceDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                deleted++;
            }
            catch
            {
                skipped++;
            }
        }

        // Remove any now-empty subdirectories.
        foreach (var sub in Directory.GetDirectories(instanceDir, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length)) // deepest first
        {
            try { Directory.Delete(sub); } catch { }
        }

        try
        {
            Directory.Delete(instanceDir);
            _logger.LogDebug("Instance cleaned up: {Dir} ({Deleted} files)", instanceDir, deleted);
            return true;
        }
        catch
        {
            // Remaining locked files (shared inodes in use by other acclient processes).
            // CleanupStaleInstances() will retry on next startup.
            _logger.LogDebug("Instance partially cleaned ({Deleted} deleted, {Skipped} locked — will retry): {Dir}",
                deleted, skipped, instanceDir);
            return false;
        }
    }

    private static bool IsInstanceStale(string instanceDir)
    {
        var instanceDirNorm = Path.GetFullPath(instanceDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("acclient"))
        {
            try
            {
                var exePath = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) continue;

                var exeDirNorm = Path.GetFullPath(Path.GetDirectoryName(exePath)!)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();

                if (exeDirNorm == instanceDirNorm)
                    return false; // this instance is still in use
            }
            catch
            {
                // Can't read this process's module — skip it rather than keeping all dirs forever.
                // If it turns out to be using this dir, the process will still hold file locks
                // and Directory.Delete will fail safely.
                continue;
            }
            finally
            {
                proc.Dispose();
            }
        }

        return true;
    }
}
