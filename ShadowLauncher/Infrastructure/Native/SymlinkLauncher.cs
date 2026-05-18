using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Services.Dats;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Creates a per-instance "virtual install" directory using symbolic links, then
/// launches acclient.exe from that directory.
///
/// Prerequisites:
///   Creating file symlinks on Windows requires either:
///     (a) Developer Mode enabled (no elevation needed), or
///     (b) SeCreateSymbolicLinkPrivilege (Administrator / elevated process).
///   <see cref="CanCreateSymlinks"/> can be used to probe capability at runtime.
/// </summary>
public class SymlinkLauncher : InstanceLauncherBase
{
    // Windows SDK symbolic link flag values (from winbase.h)
    private const int SymlinkFlagFile = 0;
    private const int SymlinkFlagAllowUnprivilegedCreate = 2;

    public SymlinkLauncher(
        IConfigurationProvider config,
        IDatSetService datSetService,
        ILogger<SymlinkLauncher> logger)
        : base(config, datSetService, logger)
    {
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Returns true if the current process can create symbolic links.</summary>
    public static bool CanCreateSymlinks()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"sl_symtest_{Guid.NewGuid():N}");
        var linkPath = Path.Combine(testDir, "test.lnk");
        try
        {
            Directory.CreateDirectory(testDir);
            if (CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagAllowUnprivilegedCreate))
                return true;
            return CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagFile);
        }
        catch { return false; }
        finally
        {
            try { Directory.Delete(testDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Returns a multi-line diagnostic string describing exactly why symlink creation
    /// is working or failing in the current session.
    /// </summary>
    public static string DiagnoseSymlinkCapability()
    {
        var lines = new System.Text.StringBuilder();

        try { lines.AppendLine($"  User            : {Environment.UserDomainName}\\{Environment.UserName}"); }
        catch { lines.AppendLine("  User            : (unavailable)"); }

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            lines.AppendLine($"  Elevated        : {principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)}");
        }
        catch { lines.AppendLine("  Elevated        : (unavailable)"); }

        var testDir = Path.Combine(Path.GetTempPath(), $"sl_diagtest_{Guid.NewGuid():N}");
        var linkPath = Path.Combine(testDir, "test.lnk");
        try
        {
            Directory.CreateDirectory(testDir);

            bool devMode = CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagAllowUnprivilegedCreate);
            int devErr = Marshal.GetLastWin32Error();
            lines.AppendLine($"  DevMode symlink : {(devMode ? "OK" : $"FAILED (Win32={devErr} / 0x{devErr:X8})")}");
            if (File.Exists(linkPath)) File.Delete(linkPath);

            bool privMode = CreateSymbolicLink(linkPath, "dummy_target", SymlinkFlagFile);
            int privErr = Marshal.GetLastWin32Error();
            lines.AppendLine($"  Priv symlink    : {(privMode ? "OK" : $"FAILED (Win32={privErr} / 0x{privErr:X8})")}");

            if (!devMode && !privMode && privErr == 1314)
                lines.AppendLine("  Diagnosis       : ERROR_PRIVILEGE_NOT_HELD (1314) — privilege was granted but this " +
                                  "session's token predates it. A full sign-out and sign-back-in is required.");
            else if (!devMode && !privMode)
                lines.AppendLine($"  Diagnosis       : Unexpected failure — Win32={privErr}.");
        }
        catch (Exception ex) { lines.AppendLine($"  Diagnosis       : Exception during test — {ex.Message}"); }
        finally { try { Directory.Delete(testDir, recursive: true); } catch { /* best-effort */ } }

        return lines.ToString().TrimEnd();
    }

    /// <inheritdoc/>
    public override async Task<string?> PrepareInstanceAsync(
        Server server,
        IProgress<DatDownloadProgress>? downloadProgress = null)
    {
        if (!CanCreateSymlinks())
        {
            _logger.LogError(
                "Symlink creation failed for server '{Server}'.\n{Diagnosis}",
                server.Name, DiagnoseSymlinkCapability());
            return null;
        }

        var clientPath = _config.GameClientPath;
        var clientDir = Path.GetDirectoryName(clientPath)!;

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

        _logger.LogInformation("Creating symlink instance directory: {Dir}", instanceDir);

        try
        {
            foreach (var file in Directory.GetFiles(clientDir))
                CreateFileSymlink(Path.Combine(instanceDir, Path.GetFileName(file)), file);

            foreach (var datFile in KnownDatFiles)
            {
                var sourceDat = Path.Combine(datSourceDir, datFile);
                if (!File.Exists(sourceDat))
                {
                    _logger.LogWarning("DAT '{File}' missing from source '{Dir}' — keeping retail symlink", datFile, datSourceDir);
                    continue;
                }
                var linkPath = Path.Combine(instanceDir, datFile);
                if (File.Exists(linkPath)) File.Delete(linkPath);
                CreateFileSymlink(linkPath, sourceDat);
            }

            var customClient = Path.Combine(datSourceDir, "acclient.exe");
            if (File.Exists(customClient))
            {
                var exeLink = Path.Combine(instanceDir, "acclient.exe");
                if (File.Exists(exeLink)) File.Delete(exeLink);
                CreateFileSymlink(exeLink, customClient);
                _logger.LogInformation("Using custom acclient.exe from DAT set: {Path}", customClient);
            }

            return instanceDir;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare symlink instance at {Dir}", instanceDir);
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

    private static void CreateFileSymlink(string linkPath, string targetPath)
    {
        if (CreateSymbolicLink(linkPath, targetPath, SymlinkFlagAllowUnprivilegedCreate))
            return;
        if (CreateSymbolicLink(linkPath, targetPath, SymlinkFlagFile))
            return;

        var error = Marshal.GetLastWin32Error();
        throw new InvalidOperationException(
            $"CreateSymbolicLink failed for '{linkPath}' → '{targetPath}' (Win32 error {error}).");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateSymbolicLink(
        string lpSymlinkFileName,
        string lpTargetFileName,
        int dwFlags);
}
