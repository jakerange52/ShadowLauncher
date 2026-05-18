using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Shared base for <see cref="SymlinkLauncher"/> and <see cref="HardLinkLauncher"/>.
/// Provides common instance-directory management: cleanup, stale-instance detection,
/// and the list of known DAT filenames acclient.exe looks for at runtime.
/// </summary>
public abstract class InstanceLauncherBase : IInstancePreparer
{
    // DAT filenames acclient.exe looks for in its working directory.
    internal static readonly string[] KnownDatFiles =
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
    public abstract Task<string?> PrepareInstanceAsync(
        Server server,
        IProgress<DatDownloadProgress>? downloadProgress = null);

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
            if (IsInstanceStale(dir))
            {
                CleanupInstance(dir);
                _logger.LogInformation("Cleaned up stale instance: {Dir}", dir);
            }
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

    protected void CleanupInstance(string instanceDir)
    {
        try
        {
            if (!Directory.Exists(instanceDir)) return;
            // Hard-linked files inherit read-only attributes from the source — strip them
            // before deletion so Directory.Delete doesn't throw UnauthorizedAccessException.
            foreach (var file in Directory.GetFiles(instanceDir))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(instanceDir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up instance directory: {Dir}", instanceDir);
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
                if (string.IsNullOrEmpty(exePath)) return false;

                var exeDirNorm = Path.GetFullPath(Path.GetDirectoryName(exePath)!)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();

                if (exeDirNorm == instanceDirNorm)
                    return false;
            }
            catch
            {
                return false; // can't confirm — conservatively keep it
            }
            finally
            {
                proc.Dispose();
            }
        }

        return true;
    }
}
