namespace ShadowLauncher.Core.Interfaces;

/// <summary>
/// Result of a successful <see cref="IInstancePreparer.PrepareInstanceAsync"/> call.
/// </summary>
/// <param name="ExePath">Stable path to the acclient.exe to launch (ACBase or DAT-set custom client).</param>
/// <param name="WorkingDir">Per-instance directory containing hard-linked DLLs and DAT files.</param>
public record InstanceEnvironment(string ExePath, string WorkingDir);

/// <summary>
/// Abstracts the strategy used to prepare a per-instance AC directory before launch.
/// Implementations: <c>SymlinkLauncher</c> (requires symlink privilege / Dev Mode),
/// <c>HardLinkLauncher</c> (no privileges required).
/// </summary>
public interface IInstancePreparer
{
    /// <summary>
    /// Ensures required DAT files are ready and creates a per-instance directory.
    /// Returns an <see cref="InstanceEnvironment"/> on success, or null on failure.
    /// </summary>
    Task<InstanceEnvironment?> PrepareInstanceAsync(ShadowLauncher.Core.Models.Server server);

    /// <summary>Watches the process and deletes the instance directory when it exits.</summary>
    Task WatchAndCleanupAsync(System.Diagnostics.Process process, string instanceDir);

    /// <summary>Deletes stale instance directories left from a previous session.</summary>
    void CleanupStaleInstances();
}
