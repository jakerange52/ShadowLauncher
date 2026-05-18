namespace ShadowLauncher.Core.Interfaces;

/// <summary>
/// Abstracts the strategy used to prepare a per-instance AC directory before launch.
/// Implementations: <c>SymlinkLauncher</c> (requires symlink privilege / Dev Mode),
/// <c>HardLinkLauncher</c> (no privileges required).
/// </summary>
public interface IInstancePreparer
{
    /// <summary>
    /// Ensures required DAT files are ready and creates a per-instance directory.
    /// Returns the instance directory path on success, or null on failure.
    /// </summary>
    Task<string?> PrepareInstanceAsync(
        ShadowLauncher.Core.Models.Server server,
        IProgress<ShadowLauncher.Services.Dats.DatDownloadProgress>? downloadProgress = null);

    /// <summary>Watches the process and deletes the instance directory when it exits.</summary>
    Task WatchAndCleanupAsync(System.Diagnostics.Process process, string instanceDir);

    /// <summary>Deletes stale instance directories left from a previous session.</summary>
    void CleanupStaleInstances();
}
