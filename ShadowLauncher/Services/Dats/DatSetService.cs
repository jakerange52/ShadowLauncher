using System.IO.Compression;
using System.Net.Http;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Infrastructure.WebServices;

namespace ShadowLauncher.Services.Dats;

/// <inheritdoc/>
public class DatSetService : IDatSetService
{
    private readonly DatRegistryDownloader _downloader;
    private readonly IConfigurationProvider _config;
    private readonly ILogger<DatSetService> _logger;

    // In-memory cache so we don't re-fetch on every call within a session.
    private IReadOnlyList<DatSet>? _cachedSets;

    public DatSetService(
        DatRegistryDownloader downloader,
        IConfigurationProvider config,
        ILogger<DatSetService> logger)
    {
        _downloader = downloader;
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DatSet>> GetAvailableDatSetsAsync()
    {
        if (_cachedSets is not null)
            return _cachedSets;

        _cachedSets = await _downloader.FetchDatSetsAsync();
        return _cachedSets;
    }

    /// <inheritdoc/>
    public async Task<DatSet?> GetDatSetAsync(string datSetId)
    {
        if (string.IsNullOrWhiteSpace(datSetId))
            return null;

        var sets = await GetAvailableDatSetsAsync();
        return sets.FirstOrDefault(s => string.Equals(s.Id, datSetId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public async Task<string?> ResolveDatSetIdForServerAsync(string serverName)
    {
        var sets = await GetAvailableDatSetsAsync();
        var match = sets.FirstOrDefault(s =>
            s.ServerNames.Any(n => string.Equals(n, serverName, StringComparison.OrdinalIgnoreCase)));
        return match?.Id;
    }

    /// <inheritdoc/>
    public async Task RefreshRegistryAsync()
    {
        _cachedSets = null; // bust in-memory cache
        _cachedSets = await _downloader.FetchDatSetsAsync();
    }

    /// <inheritdoc/>
    public string GetLocalDatSetPath(string datSetId)
        => Path.Combine(_config.DatSetsDirectory, datSetId);

    /// <inheritdoc/>
    public string GetLocalDatSetPathForServer(Server server)
    {
        // Local directory takes priority — it's already on disk, no download needed.
        if (!string.IsNullOrWhiteSpace(server.CustomDatRegistryPath))
            return server.CustomDatRegistryPath;

        // Zip URL: DATs land in the standard cache dir keyed by server name.
        if (!string.IsNullOrWhiteSpace(server.CustomDatZipUrl))
            return Path.Combine(_config.DatSetsDirectory, SanitiseId(server.Name));

        return GetLocalDatSetPath(server.DatSetId ?? string.Empty);
    }

    /// <inheritdoc/>
    public async Task EnsureCustomDatSourceReadyAsync(Server server, IProgress<DatDownloadProgress>? progress = null)
    {
        // Local path: just verify it exists — no download required.
        if (!string.IsNullOrWhiteSpace(server.CustomDatRegistryPath))
        {
            if (!Directory.Exists(server.CustomDatRegistryPath))
                throw new InvalidOperationException(
                    $"Custom DAT path for '{server.Name}' does not exist: {server.CustomDatRegistryPath}");
            _logger.LogInformation("Custom DAT source for '{Server}' is local path: {Path}",
                server.Name, server.CustomDatRegistryPath);
            return;
        }

        // Zip URL: download if not already cached.
        if (!string.IsNullOrWhiteSpace(server.CustomDatZipUrl))
        {
            var localDir = Path.Combine(_config.DatSetsDirectory, SanitiseId(server.Name));

            // Consider it ready only if all known DAT files are already present.
            // A partial cache will be completed (the zip re-extracted for missing files),
            // but ExractDatZip will skip any already-present files so nothing is overwritten.
            if (KnownAcFileNames
                    .Where(f => !f.Equals("acclient.exe", StringComparison.OrdinalIgnoreCase))
                    .All(f => File.Exists(Path.Combine(localDir, f))))
            {
                _logger.LogInformation("Custom DAT zip for '{Server}' already cached at {Dir}",
                    server.Name, localDir);
                return;
            }

            _logger.LogInformation("Downloading custom DAT zip for '{Server}' from {Url}",
                server.Name, server.CustomDatZipUrl);
            Directory.CreateDirectory(localDir);

            using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            var tempZip = Path.Combine(localDir, "__custom_download.zip");

            try
            {
                await DownloadRawAsync(http, server.CustomDatZipUrl, tempZip, progress);
                _logger.LogInformation("Extracting custom DAT zip for '{Server}'", server.Name);
                ExtractDatZip(tempZip, localDir, new DatSet()); // accept all known AC filenames
            }
            finally
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }

            _logger.LogInformation("Custom DAT zip for '{Server}' ready at {Dir}", server.Name, localDir);
            return;
        }

        throw new InvalidOperationException(
            $"Server '{server.Name}' has no custom DAT source configured. " +
            "Set either a local directory path or a zip download URL in the server settings.");
    }

    /// <inheritdoc/>
    public async Task<bool> IsDatSetReadyAsync(string datSetId)
    {
        if (IsRetailSet(datSetId))
            return true;

        var set = await GetDatSetAsync(datSetId);
        if (set is null)
        {
            _logger.LogWarning("DAT set '{Id}' not found in registry", datSetId);
            return false;
        }

        var ready = set.IsFullyDownloaded(_config.DatSetsDirectory);

        if (!ready)
            _logger.LogWarning("DAT set '{Id}' is not fully downloaded", datSetId);

        return ready;
    }

    /// <inheritdoc/>
    public async Task DownloadMissingFilesAsync(string datSetId, IProgress<DatDownloadProgress>? progress = null)
    {
        if (IsRetailSet(datSetId))
            return;

        var set = await GetDatSetAsync(datSetId);
        if (set is null)
            throw new InvalidOperationException($"DAT set '{datSetId}' not found in registry.");

        var localDir = GetLocalDatSetPath(datSetId);

        // ── Zip path ───────────────────────────────────────────────────────────
        // If the set declares a zip URL and any expected DAT files are missing,
        // download and extract the zip. Only the recognised DAT filenames are kept.
        if (!string.IsNullOrWhiteSpace(set.ZipUrl) && !set.IsFullyDownloaded(_config.DatSetsDirectory))
        {
            _logger.LogInformation("Downloading DAT zip for '{Id}' from {Url}", set.Id, set.ZipUrl);
            Directory.CreateDirectory(localDir);

            using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            var tempZip = Path.Combine(localDir, "__download.zip");

            try
            {
                await DownloadRawAsync(http, set.ZipUrl, tempZip, progress);

                if (!string.IsNullOrWhiteSpace(set.ZipSha256))
                    VerifyChecksum(tempZip, set.ZipSha256, "zip archive");

                _logger.LogInformation("Extracting DAT zip for '{Id}'", set.Id);
                ExtractDatZip(tempZip, localDir, set);
            }
            finally
            {
                if (File.Exists(tempZip))
                    File.Delete(tempZip);
            }

            _logger.LogInformation("DAT set '{Id}' zip extraction complete", set.Id);
            return;
        }

        // No zip URL and no individual file URLs — nothing to download.
        _logger.LogInformation("DAT set '{Id}' has no downloadable source configured", datSetId);
    }

    /// <inheritdoc/>
    public Task CompleteDatCacheFromRetailAsync(string datCacheDir, string retailClientDir)
    {
        Directory.CreateDirectory(datCacheDir);

        foreach (var datFile in SymlinkLauncher.KnownDatFiles)
        {
            var dest = Path.Combine(datCacheDir, datFile);
            if (File.Exists(dest)) continue;

            var retail = Path.Combine(retailClientDir, datFile);
            if (!File.Exists(retail))
            {
                _logger.LogWarning(
                    "Cannot complete DAT cache: '{File}' not found in retail directory '{Dir}'",
                    datFile, retailClientDir);
                continue;
            }

            _logger.LogInformation(
                "Copying retail DAT '{File}' into cache '{Dir}' to complete the set",
                datFile, datCacheDir);
            File.Copy(retail, dest);
        }

        return Task.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool IsRetailSet(string? id)
        => string.IsNullOrWhiteSpace(id)
        || string.Equals(id, "retail", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts a server name into a safe directory name for use as a local cache key.
    /// </summary>
    private static string SanitiseId(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .ToLowerInvariant()
            .Trim('_', ' ');
    }

    /// <summary>
    /// Downloads a URL to a local file, reporting byte-level progress via <paramref name="progress"/>.
    /// The destination file is deleted on failure so no partial file is ever left behind.
    /// </summary>
    private static async Task DownloadRawAsync(
        HttpClient http,
        string url,
        string destPath,
        IProgress<DatDownloadProgress>? progress)
    {
        var fileName = Path.GetFileName(destPath);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            long received = 0;

            // Emit an initial report so the UI shows the filename immediately.
            progress?.Report(new DatDownloadProgress(fileName, 0, totalBytes));

            await using var src = await response.Content.ReadAsStreamAsync();
            await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read));
                received += read;
                progress?.Report(new DatDownloadProgress(fileName, received, totalBytes));
            }
        }
        catch
        {
            if (File.Exists(destPath))
                try { File.Delete(destPath); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Extracts only the recognised DAT filenames from a zip into <paramref name="destDir"/>.
    /// Files in the zip not matching the set's known filenames are ignored.
    /// The zip may contain files in subdirectories — only the filename is matched.
    /// </summary>
    private static readonly HashSet<string> KnownAcFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "client_portal.dat",
        "client_cell_1.dat",
        "client_local_English.dat",
        "client_highres.dat",
        "acclient.exe",
    };

    /// <summary>
    /// Extracts only the recognised DAT filenames from a zip into <paramref name="destDir"/>.
    /// Files in the zip not matching the set's known filenames are ignored.
    /// The zip may contain files in subdirectories — only the filename is matched.
    /// </summary>
    private void ExtractDatZip(string zipPath, string destDir, DatSet set)
    {
        // If the registry entry has explicit File entries, match only those.
        // Otherwise (zip-only entry with no File children) accept any known AC filename.
        var knownNames = set.Files.Count > 0
            ? set.Files.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : KnownAcFileNames;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            // Match by filename only, ignoring any directory structure inside the zip.
            var entryName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(entryName)) continue;
            if (!knownNames.Contains(entryName)) continue;

            var destPath = Path.Combine(destDir, entryName);
            if (File.Exists(destPath))
            {
                _logger.LogDebug("Skipping '{File}' — already present in cache", entryName);
                continue;
            }

            _logger.LogInformation("Extracting {File} from zip", entryName);
            entry.ExtractToFile(destPath, overwrite: false);
        }
    }

    private static void VerifyChecksum(string filePath, string expectedSha256, string fileName)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = Convert.ToHexString(sha.ComputeHash(stream));

        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(filePath);
            throw new InvalidDataException(
                $"Checksum mismatch for {fileName}: expected {expectedSha256}, got {hash}. File deleted.");
        }
    }
}
