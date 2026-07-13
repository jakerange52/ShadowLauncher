using Microsoft.Win32;

namespace ShadowLauncher.Infrastructure.Decal;

/// <summary>
/// Installs ShadowFilter into the user's Decal Plugins folder and registers it with Decal.
/// Used by first-run fallback when MSI registration did not run or Decal was installed later.
/// </summary>
public static class ShadowFilterDecalRegistration
{
    public static bool IsRegisteredAndEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ShadowFilterPluginInfo.RegistryKeyPath, writable: false);
            if (key is null)
                return false;

            var enabled = key.GetValue("Enabled");
            if (enabled is int i)
                return i != 0;
            if (enabled is not null && int.TryParse(enabled.ToString(), out var parsed))
                return parsed != 0;

            return key.GetValue("File") is string path && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryInstallFromShadowLauncherFolder(string installFolder)
    {
        try
        {
            var sourceDir = Path.Combine(installFolder, ShadowFilterPluginInfo.FilterName);
            if (!Directory.Exists(sourceDir))
                return false;

            var destDir = GetDecalPluginsFolder();
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
            {
                var dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
                TryUnblock(dest);
            }

            var pluginDll = Path.Combine(destDir, $"{ShadowFilterPluginInfo.FilterName}.dll");
            if (!File.Exists(pluginDll))
                return false;

            Register(pluginDll);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Register(string pluginDllPath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ShadowFilterPluginInfo.RegistryKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open Decal plugin registry key.");

        key.SetValue("File", pluginDllPath, RegistryValueKind.String);
        key.SetValue("Enabled", 1, RegistryValueKind.DWord);
        key.SetValue("Name", ShadowFilterPluginInfo.FilterName, RegistryValueKind.String);
        key.SetValue("Version", "1.0.0.0", RegistryValueKind.String);
        TryUnblock(pluginDllPath);
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ShadowFilterPluginInfo.RegistryKeyPath, throwOnMissingSubKey: false);
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
