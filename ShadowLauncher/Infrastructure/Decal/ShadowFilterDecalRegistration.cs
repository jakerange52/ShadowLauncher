using Microsoft.Win32;

namespace ShadowLauncher.Infrastructure.Decal;

/// <summary>
/// Registers ShadowFilter with Decal as a network filter (HKLM NetworkFilters), matching
/// ThwargFilter / Mag-Filter. Used by first-run fallback when MSI registration did not run
/// or Decal was installed later. HKLM writes require elevation — failures are non-fatal.
/// </summary>
public static class ShadowFilterDecalRegistration
{
    public static bool IsRegisteredAndEnabled()
    {
        try
        {
            using var key = OpenNetworkFiltersKey(writable: false);
            if (key is null)
                return false;

            if (!IsEnabledValue(key.GetValue("Enabled")))
                return false;

            var path = key.GetValue("Path") as string;
            var assembly = key.GetValue("Assembly") as string ?? ShadowFilterPluginInfo.AssemblyFileName;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            return File.Exists(Path.Combine(path, assembly));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers <c>installFolder\ShadowFilter\</c> with Decal (Path = that folder, like Thwarg).
    /// If HKLM write fails (no elevation), stages DLLs under Documents\Decal Plugins for a manual Decal Agent add.
    /// </summary>
    public static bool TryInstallFromShadowLauncherFolder(string installFolder)
    {
        try
        {
            var sourceDir = Path.Combine(installFolder, ShadowFilterPluginInfo.FilterName);
            var pluginDll = Path.Combine(sourceDir, ShadowFilterPluginInfo.AssemblyFileName);
            if (!File.Exists(pluginDll))
                return false;

            try
            {
                Register(sourceDir);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                StageDocumentsCopy(sourceDir);
                return false;
            }
            catch (InvalidOperationException)
            {
                StageDocumentsCopy(sourceDir);
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers <paramref name="pluginFolder"/> with Decal as a network filter.
    /// Folder must contain ShadowFilter.dll. Requires elevation for HKLM.
    /// </summary>
    public static void Register(string pluginFolder)
    {
        if (string.IsNullOrWhiteSpace(pluginFolder) || !Directory.Exists(pluginFolder))
            throw new InvalidOperationException("ShadowFilter plugin folder is missing.");

        var pluginDll = Path.Combine(pluginFolder, ShadowFilterPluginInfo.AssemblyFileName);
        if (!File.Exists(pluginDll))
            throw new InvalidOperationException($"Missing {ShadowFilterPluginInfo.AssemblyFileName} in {pluginFolder}.");

        var normalized = pluginFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        using var key = OpenNetworkFiltersKey(writable: true)
            ?? throw new UnauthorizedAccessException("Could not open Decal NetworkFilters registry key (elevation required).");

        key.SetValue(null, ShadowFilterPluginInfo.FilterName, RegistryValueKind.String);
        key.SetValue("Path", normalized, RegistryValueKind.String);
        key.SetValue("Assembly", ShadowFilterPluginInfo.AssemblyFileName, RegistryValueKind.String);
        key.SetValue("Object", ShadowFilterPluginInfo.ObjectTypeName, RegistryValueKind.String);
        key.SetValue("Surrogate", ShadowFilterPluginInfo.SurrogateGuid, RegistryValueKind.String);
        key.SetValue("Enabled", 1, RegistryValueKind.DWord);
        TryUnblock(pluginDll);
        TryRemoveLegacyPluginsKey();
    }

    public static void Unregister()
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            baseKey.DeleteSubKeyTree(ShadowFilterPluginInfo.NetworkFiltersKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best effort — may lack elevation.
        }

        TryRemoveLegacyPluginsKey();

        try
        {
            var destDir = GetDecalPluginsFolder();
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    public static string GetDecalPluginsFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Path.Combine(documents, "Decal Plugins", ShadowFilterPluginInfo.FilterName);
    }

    private static void StageDocumentsCopy(string sourceDir)
    {
        try
        {
            var destDir = GetDecalPluginsFolder();
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
            {
                var dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
                TryUnblock(dest);
            }
        }
        catch
        {
            // Best effort — Decal Agent can still be pointed at INSTALLFOLDER\ShadowFilter\.
        }
    }

    private static RegistryKey? OpenNetworkFiltersKey(bool writable)
    {
        var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        try
        {
            return writable
                ? baseKey.CreateSubKey(ShadowFilterPluginInfo.NetworkFiltersKeyPath, writable: true)
                : baseKey.OpenSubKey(ShadowFilterPluginInfo.NetworkFiltersKeyPath, writable: false);
        }
        finally
        {
            baseKey.Dispose();
        }
    }

    private static void TryRemoveLegacyPluginsKey()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ShadowFilterPluginInfo.LegacyPluginsKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // Non-fatal.
        }
    }

    private static bool IsEnabledValue(object? enabled)
    {
        if (enabled is int i)
            return i != 0;
        if (enabled is not null && int.TryParse(enabled.ToString(), out var parsed))
            return parsed != 0;
        return false;
    }

    private static void TryUnblock(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

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
