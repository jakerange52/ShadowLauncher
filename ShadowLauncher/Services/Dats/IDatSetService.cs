using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Dats;

/// <summary>
/// Manages the available DAT sets: fetching the remote registry, caching DAT sets locally,
/// downloading missing files, and providing the correct local path for a given set ID.
/// </summary>
public interface IDatSetService
{
    /// <summary>
    /// Downloads and parses the remote DatRegistry.xml, returning all known DAT sets.
    /// Falls back to any locally cached registry on network failure.
    /// </summary>
    Task<IReadOnlyList<DatSet>> GetAvailableDatSetsAsync();

    /// <summary>
    /// Returns the DatSet for the given ID, or null if it is not in the registry.
    /// </summary>
    Task<DatSet?> GetDatSetAsync(string datSetId);

    /// <summary>
    /// Returns the full local directory path where the files for a given DAT set
    /// are stored (or should be stored). The directory may not exist yet.
    /// </summary>
    string GetLocalDatSetPath(string datSetId);

    /// <summary>
    /// Returns true if all files for the given DAT set are present locally.
    /// </summary>
    Task<bool> IsDatSetReadyAsync(string datSetId);

    /// <summary>
    /// Downloads any missing downloadable files for the given DAT set.
    /// Reports progress through the optional callback (0.0 – 1.0 per file).
    /// </summary>
    Task DownloadMissingFilesAsync(string datSetId, IProgress<DatDownloadProgress>? progress = null);

    /// <summary>
    /// Returns the DAT set ID required by a server with the given name,
    /// or null if no DAT set in the registry claims that server.
    /// </summary>
    Task<string?> ResolveDatSetIdForServerAsync(string serverName);

    /// <summary>
    /// Forces a fresh download of the DatRegistry.xml, busting the in-memory cache.
    /// Call on startup so checksums and server mappings are always current.
    /// </summary>
    Task RefreshRegistryAsync();
}

/// <summary>Progress report emitted during a DAT download.</summary>
/// <param name="FileName">The zip or file currently being downloaded.</param>
/// <param name="BytesReceived">Bytes received so far.</param>
/// <param name="TotalBytes">Total expected bytes (0 if unknown).</param>
public record DatDownloadProgress(
    string FileName,
    long BytesReceived,
    long TotalBytes);
