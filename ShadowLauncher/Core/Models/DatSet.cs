namespace ShadowLauncher.Core.Models;

/// <summary>
/// A named, versioned set of AC DAT files that describes a complete client data install.
///
/// The "retail" set is the baseline (end-of-retail AC patch). Any server that uses
/// non-standard DATs (e.g. a Dark Majesty expansion set) will have its own entry.
///
/// DAT sets are defined in DatRegistry.xml and cached locally under
/// %LocalAppData%\ShadowLauncher\DatSets\{Id}\.
/// </summary>
public class DatSet
{
    /// <summary>
    /// Stable, URL-safe identifier, e.g. "retail", "dark-majesty", "seedsow-custom".
    /// Servers reference this ID in their own definition to declare which DATs they require.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable display name shown in the UI.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Short description shown in the server details card and DAT manager.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Semantic version string (e.g. "1.0", "17.3.2") used to detect when a cached
    /// local copy is outdated and needs re-downloading.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Optional URL to a zip archive containing all DAT files for this set.
    /// May be a direct download URL or a GitHub releases URL
    /// (e.g. https://github.com/owner/repo/releases/latest), in which case
    /// the launcher resolves the latest release asset via the GitHub API and
    /// uses the release tag to detect when a re-download is needed.
    /// </summary>
    public string ZipUrl { get; init; } = string.Empty;

    /// <summary>The DAT files that make up this set.</summary>
    public List<DatFile> Files { get; init; } = [];

    /// <summary>
    /// Server names (case-insensitive) that require this DAT set.
    /// Used to auto-assign DatSetId when a matching server is added.
    /// </summary>
    public List<string> ServerNames { get; init; } = [];
}
