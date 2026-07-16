using System.IO;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace ShadowLauncher.Installer.CustomActions;

/// <summary>
/// Registers ShadowFilter as a Decal network filter (HKLM NetworkFilters), matching
/// ThwargFilter / Mag-Filter / UtilityBelt. Runs elevated (Impersonate=no) so HKLM writes succeed.
/// Path points at INSTALLFOLDER\ShadowFilter\ — no Documents copy (same pattern as Thwarg).
/// </summary>
public static class ShadowFilterDecalActions
{
    private const string FilterName = "ShadowFilter";
    private const string AssemblyFileName = "ShadowFilter.dll";
    private const string ObjectTypeName = "ShadowFilter.FilterCore";
    private const string AssemblyGuid = "A8F3C2D1-4E5B-6A7C-8D9E-0F1A2B3C4D5E";
    private const string SurrogateGuid = "{71A69713-6593-47EC-0002-0000000DECA1}";
    private const string NetworkFiltersKeyPath = @"Software\Decal\NetworkFilters\" + AssemblyGuid;
    private const string LegacyPluginsKeyPath = @"Software\Decal\Plugins\" + AssemblyGuid;

    [CustomAction]
    public static ActionResult InstallShadowFilterDecalPlugin(Session session)
    {
        session.Log("InstallShadowFilterDecalPlugin: begin");
        try
        {
            var sourceDir = session.CustomActionData["SOURCE_DIR"];
            if (string.IsNullOrWhiteSpace(sourceDir))
            {
                session.Log("InstallShadowFilterDecalPlugin: SOURCE_DIR missing");
                return ActionResult.Success;
            }

            sourceDir = sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pluginDll = Path.Combine(sourceDir, AssemblyFileName);
            if (!File.Exists(pluginDll))
            {
                session.Log($"InstallShadowFilterDecalPlugin: plugin DLL missing at {pluginDll}");
                return ActionResult.Success;
            }

            TryUnblock(pluginDll);
            foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
                TryUnblock(file);

            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = baseKey.CreateSubKey(NetworkFiltersKeyPath, writable: true);
            if (key is null)
            {
                session.Log("InstallShadowFilterDecalPlugin: failed to open NetworkFilters key");
                return ActionResult.Success;
            }

            key.SetValue(null, FilterName, RegistryValueKind.String);
            key.SetValue("Path", sourceDir, RegistryValueKind.String);
            key.SetValue("Assembly", AssemblyFileName, RegistryValueKind.String);
            key.SetValue("Object", ObjectTypeName, RegistryValueKind.String);
            key.SetValue("Surrogate", SurrogateGuid, RegistryValueKind.String);
            key.SetValue("Enabled", 1, RegistryValueKind.DWord);
            session.Log($"InstallShadowFilterDecalPlugin: registered NetworkFilters Path={sourceDir}");

            TryRemoveLegacyPluginsKey(session);
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"InstallShadowFilterDecalPlugin: failed — {ex.Message}");
            return ActionResult.Success;
        }
    }

    [CustomAction]
    public static ActionResult UninstallShadowFilterDecalPlugin(Session session)
    {
        session.Log("UninstallShadowFilterDecalPlugin: begin");
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            baseKey.DeleteSubKeyTree(NetworkFiltersKeyPath, throwOnMissingSubKey: false);
            session.Log("UninstallShadowFilterDecalPlugin: removed NetworkFilters key");

            TryRemoveLegacyPluginsKey(session);

            // Elevated MSI context: Personal may be SYSTEM's profile. Prefer the installing
            // user's Documents via CustomActionData if we ever need it; for now only clean
            // the common OneDrive/user Documents path is best-effort and often skipped.
            try
            {
                foreach (var documents in GetLikelyDocumentsFolders())
                {
                    var destDir = Path.Combine(documents, "Decal Plugins", FilterName);
                    if (Directory.Exists(destDir))
                        Directory.Delete(destDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                session.Log($"UninstallShadowFilterDecalPlugin: Documents cleanup skipped — {ex.Message}");
            }

            session.Log("UninstallShadowFilterDecalPlugin: complete");
        }
        catch (Exception ex)
        {
            session.Log($"UninstallShadowFilterDecalPlugin: failed — {ex.Message}");
        }

        return ActionResult.Success;
    }

    private static IEnumerable<string> GetLikelyDocumentsFolders()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Consider(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                seen.Add(path);
        }

        Consider(Environment.GetFolderPath(Environment.SpecialFolder.Personal));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            Consider(Path.Combine(userProfile, "Documents"));
            Consider(Path.Combine(userProfile, "OneDrive", "Documents"));
        }

        // When running as SYSTEM, probe interactive user profiles for leftover Documents copies.
        try
        {
            var usersRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "..", "Users"));
            if (Directory.Exists(usersRoot))
            {
                foreach (var userDir in Directory.GetDirectories(usersRoot))
                {
                    var name = Path.GetFileName(userDir);
                    if (name is "Public" or "Default" or "Default User" or "All Users")
                        continue;
                    Consider(Path.Combine(userDir, "Documents"));
                    Consider(Path.Combine(userDir, "OneDrive", "Documents"));
                }
            }
        }
        catch
        {
            // Best effort.
        }

        return seen;
    }

    private static void TryRemoveLegacyPluginsKey(Session session)
    {
        try
        {
            // Legacy key was per-user; deferred CA may run as SYSTEM — also try HKCU of the installing user is unreliable.
            // Clear machine-wide if present, and current-user when impersonation context allows.
            Registry.CurrentUser.DeleteSubKeyTree(LegacyPluginsKeyPath, throwOnMissingSubKey: false);
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            hklm.DeleteSubKeyTree(LegacyPluginsKeyPath, throwOnMissingSubKey: false);
            session.Log("Cleared legacy Plugins key if present");
        }
        catch (Exception ex)
        {
            session.Log($"Legacy Plugins key cleanup skipped — {ex.Message}");
        }
    }

    private static void TryUnblock(string path)
    {
        try
        {
            var zoneFile = path + ":Zone.Identifier";
            if (File.Exists(zoneFile))
                File.Delete(zoneFile);
        }
        catch
        {
            // Non-fatal.
        }
    }
}
