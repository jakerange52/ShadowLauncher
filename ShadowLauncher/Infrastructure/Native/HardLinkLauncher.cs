using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Creates a per-instance AC directory using hard links — a zero-privilege alternative
/// to <see cref="SymlinkLauncher"/>.
///
/// Hard links require both the link and the target to be on the same volume.
/// If the AC install is under a protected path (Program Files), <see cref="FirstRunService"/>
/// copies it to <c>%LocalAppData%\ShadowLauncher\ACBase\</c> first so all files land on
/// the same volume as the instance directory.
///
/// Directory layout created per launch:
///   %LocalAppData%\ShadowLauncher\Instances\{guid}\
///       acclient.exe          → hard link → ACBase (or DAT set override)
///       client_portal.dat     → hard link → DatSet cache or ACBase
///       client_cell_1.dat     → hard link → ...
///       client_local_English.dat → hard link → ...
///       client_highres.dat    → hard link → ... (if present)
///       *.dll / other files   → hard links → ACBase
/// </summary>
public class HardLinkLauncher : InstanceLauncherBase
{
    private readonly string _acBaseDir;

    public HardLinkLauncher(
        IConfigurationProvider config,
        IDatSetService datSetService,
        ILogger<HardLinkLauncher> logger)
        : base(config, datSetService, logger)
    {
        // ACBase is either the copy under LocalAppData (protected installs) or the
        // original install dir (custom path). Resolved by FirstRunService before first launch.
        var hardLinkBasePath = config.GetSetting("HardLinkBasePath");
        _acBaseDir = string.IsNullOrWhiteSpace(hardLinkBasePath)
            ? Path.GetDirectoryName(config.GameClientPath) ?? string.Empty
            : hardLinkBasePath;
    }

    /// <inheritdoc/>
    public override async Task<string?> PrepareInstanceAsync(
        Server server,
        IProgress<DatDownloadProgress>? downloadProgress = null)
    {
        var clientDir = _acBaseDir;

        // If the stored DatSetId is missing (server added before the registry had the mapping),
        // do a live lookup so we don't silently fall back to retail DATs.
        var effectiveServer = server; // mutating DatSetId on the local reference only
        if (string.IsNullOrWhiteSpace(server.DatSetId)
            && string.IsNullOrWhiteSpace(server.CustomDatRegistryPath)
            && string.IsNullOrWhiteSpace(server.CustomDatZipUrl))
        {
            var resolvedId = await _datSetService.ResolveDatSetIdForServerAsync(server.Name);
            if (!string.IsNullOrWhiteSpace(resolvedId))
            {
                _logger.LogInformation("Resolved DatSetId '{Id}' for server '{Server}' via live registry lookup", resolvedId, server.Name);
                effectiveServer.DatSetId = resolvedId;
            }
        }

        var datSourceDir = ResolveDataSourceDir(effectiveServer, clientDir);

        var instanceDir = CreateInstanceDirectory();

        if (!string.Equals(datSourceDir, clientDir, StringComparison.OrdinalIgnoreCase))
            await _datSetService.CompleteDatCacheFromRetailAsync(datSourceDir, clientDir);

        _logger.LogInformation("Creating hard-link instance at {Dir} (base={Base}, datSource={DatSource})",
            instanceDir, clientDir, datSourceDir);

        try
        {
            // Step 1: hard link every non-DAT, non-acclient file from the AC base directory.
            // DAT files and acclient.exe are skipped here — steps 2 and 3 create the correct
            // hard links directly from datSourceDir, avoiding a delete-and-recreate that would
            // fail if a running acclient process has the shared inode open.
            var datFileSet = new HashSet<string>(KnownDatFiles, StringComparer.OrdinalIgnoreCase) { "acclient.exe" };
            foreach (var file in Directory.GetFiles(clientDir))
            {
                if (datFileSet.Contains(Path.GetFileName(file))) continue;
                CreateHardLinkOrThrow(Path.Combine(instanceDir, Path.GetFileName(file)), file);
            }

            // Step 2: override DAT hard links with files from the DAT set cache.
            foreach (var datFile in KnownDatFiles)
            {
                var sourceDat = Path.Combine(datSourceDir, datFile);
                if (!File.Exists(sourceDat))
                {
                    _logger.LogWarning("DAT '{File}' missing from source '{Dir}' — keeping ACBase hard link", datFile, datSourceDir);
                    continue;
                }
                CreateHardLinkOrThrow(Path.Combine(instanceDir, datFile), sourceDat);
            }

            // Step 3: override acclient.exe with a custom one from the DAT set cache if present,
            // otherwise link the base acclient.exe.
            var customClient = Path.Combine(datSourceDir, "acclient.exe");
            var baseClient = Path.Combine(clientDir, "acclient.exe");
            var exeLink = Path.Combine(instanceDir, "acclient.exe");
            if (File.Exists(customClient))
            {
                CreateHardLinkOrThrow(exeLink, customClient);
                _logger.LogInformation("Using custom acclient.exe from DAT set: {Path}", customClient);
            }
            else if (File.Exists(baseClient))
            {
                CreateHardLinkOrThrow(exeLink, baseClient);
            }

            _logger.LogInformation("Hard-link instance ready: {Dir}", instanceDir);
            return instanceDir;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare hard-link instance at {Dir}", instanceDir);
            CleanupInstance(instanceDir);
            return null;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private string ResolveDataSourceDir(Server server, string clientDir)
    {
        var datSetId = server.DatSetId;
        bool hasCustomSource = !string.IsNullOrWhiteSpace(server.CustomDatRegistryPath)
                            || !string.IsNullOrWhiteSpace(server.CustomDatZipUrl);

        if (string.IsNullOrWhiteSpace(datSetId) ||
            string.Equals(datSetId, "retail", StringComparison.OrdinalIgnoreCase))
        {
            if (!hasCustomSource)
            {
                _logger.LogInformation("Server '{Server}' uses retail DATs", server.Name);
                return clientDir;
            }
            var dir = _datSetService.GetLocalDatSetPathForServer(server);
            _logger.LogInformation("Server '{Server}' uses custom DAT source: {Dir}", server.Name, dir);
            return dir;
        }

        var setDir = _datSetService.GetLocalDatSetPathForServer(server);
        _logger.LogInformation("Server '{Server}' requires DAT set '{DatSetId}', source: {Dir}",
            server.Name, datSetId, setDir);
        return setDir;
    }

    private void CreateHardLinkOrThrow(string linkPath, string existingFile)
    {
        _logger.LogDebug("Hard link: {Link} → {Source}", linkPath, existingFile);
        if (CreateHardLink(linkPath, existingFile, nint.Zero))
            return;

        var error = Marshal.GetLastWin32Error();
        throw new InvalidOperationException(
            $"CreateHardLink failed for '{linkPath}' → '{existingFile}' (Win32 error {error}). " +
            "Ensure both paths are on the same volume.");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        nint lpSecurityAttributes);
}
