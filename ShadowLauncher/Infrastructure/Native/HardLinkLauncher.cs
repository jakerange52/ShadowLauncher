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
    public HardLinkLauncher(
        IConfigurationProvider config,
        IDatSetService datSetService,
        ILogger<HardLinkLauncher> logger)
        : base(config, datSetService, logger)
    {
    }

    /// <summary>
    /// Resolves the AC base directory at launch time rather than at construction time,
    /// so it picks up HardLinkBasePath written by FirstRunService.PrepareHardLinkBaseAsync
    /// during app initialization (which runs after the DI container is built).
    /// </summary>
    private string ResolveAcBaseDir()
    {
        var hardLinkBasePath = _config.GetSetting("HardLinkBasePath");
        return string.IsNullOrWhiteSpace(hardLinkBasePath)
            ? Path.GetDirectoryName(_config.GameClientPath) ?? string.Empty
            : hardLinkBasePath;
    }

    /// <inheritdoc/>
    public override async Task<InstanceEnvironment?> PrepareInstanceAsync(
        Server server,
        IProgress<DatDownloadProgress>? downloadProgress = null)
    {
        var clientDir = ResolveAcBaseDir();

        if (string.IsNullOrWhiteSpace(clientDir) || !Directory.Exists(clientDir))
        {
            _logger.LogError(
                "ACBase directory is not configured or does not exist ('{Dir}'). " +
                "Set the game client path in Settings and restart the application.",
                clientDir);
            return null;
        }

        // If the stored DatSetId is missing
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
            // Step 1: hard link only the runtime-necessary files from the AC base directory,
            // including subdirectories (e.g. controls\Controls.dll).
            // We skip files that acclient opens for exclusive write access (.log, .ini, .pdb,
            // .bin, .avi, .txt, .rtf, .msi) — hard links share the same inode, so two instances
            // linking the same acclient.log would contend on it, causing a misleading DirectX error.
            // acclient.exe is intentionally excluded — we launch from its stable source path so
            // Windows Firewall only needs to learn one path, not a new GUID path each launch.
            var datFileSet = new HashSet<string>(KnownDatFiles, StringComparer.OrdinalIgnoreCase) { "acclient.exe" };
            var runtimeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dll", ".exe", ".dat", ".xsd" };
            LinkRuntimeFiles(clientDir, instanceDir, datFileSet, runtimeExtensions);

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

            // Step 3: resolve the stable acclient.exe path (never linked into instance dir).
            // Using the source path directly means Windows Firewall learns it once and never re-prompts.
            var customClient = Path.Combine(datSourceDir, "acclient.exe");
            var stableExePath = File.Exists(customClient) ? customClient : Path.Combine(clientDir, "acclient.exe");
            if (File.Exists(customClient))
                _logger.LogInformation("Using custom acclient.exe from DAT set: {Path}", customClient);

            _logger.LogInformation("Hard-link instance ready: {Dir} (exe={Exe})", instanceDir, stableExePath);
            return new InstanceEnvironment(stableExePath, instanceDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare hard-link instance at {Dir}", instanceDir);
            CleanupInstance(instanceDir);
            return null;
        }
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Recursively hard-links eligible runtime files from <paramref name="sourceDir"/> into
    /// <paramref name="destDir"/>, mirroring the subdirectory structure.
    /// Only files with extensions in <paramref name="runtimeExtensions"/> are linked;
    /// files in <paramref name="skipNames"/> (at the root level) are also excluded.
    /// Subdirectory contents are not filtered by <paramref name="skipNames"/> since DAT/exe
    /// overrides only apply to the root.
    /// </summary>
    private void LinkRuntimeFiles(
        string sourceDir,
        string destDir,
        HashSet<string> skipNames,
        HashSet<string> runtimeExtensions)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (skipNames.Contains(name)) continue;
            if (!runtimeExtensions.Contains(Path.GetExtension(file))) continue;
            CreateHardLinkOrThrow(Path.Combine(destDir, name), file);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var subName = Path.GetFileName(subDir);
            // Skip backup dirs and plugin dirs — plugins are not instance-specific
            // and the backup dir contains installer artefacts, not runtime files.
            if (string.Equals(subName, "backup", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(subName, "plugins", StringComparison.OrdinalIgnoreCase)) continue;

            var destSub = Path.Combine(destDir, subName);
            Directory.CreateDirectory(destSub);
            LinkRuntimeFiles(subDir, destSub, [], runtimeExtensions);
        }
    }

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
