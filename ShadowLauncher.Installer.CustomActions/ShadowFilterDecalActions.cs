using System.IO;
using Microsoft.Win32;
using WixToolset.Dtf.WindowsInstaller;

namespace ShadowLauncher.Installer.CustomActions;

public static class ShadowFilterDecalActions
{
    private const string FilterName = "ShadowFilter";
    private const string AssemblyGuid = "A8F3C2D1-4E5B-6A7C-8D9E-0F1A2B3C4D5E";
    private const string RegistryKeyPath = @"Software\Decal\Plugins\" + AssemblyGuid;

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

            var destDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Decal Plugins",
                FilterName);

            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
            {
                var dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
                TryUnblock(dest);
                session.Log($"InstallShadowFilterDecalPlugin: copied {dest}");
            }

            var pluginDll = Path.Combine(destDir, FilterName + ".dll");
            if (!File.Exists(pluginDll))
            {
                session.Log("InstallShadowFilterDecalPlugin: plugin DLL missing after copy");
                return ActionResult.Success;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);
            if (key is null)
            {
                session.Log("InstallShadowFilterDecalPlugin: failed to open registry key");
                return ActionResult.Success;
            }

            key.SetValue("File", pluginDll, RegistryValueKind.String);
            key.SetValue("Enabled", 1, RegistryValueKind.DWord);
            key.SetValue("Name", FilterName, RegistryValueKind.String);
            key.SetValue("Version", "1.0.0.0", RegistryValueKind.String);
            session.Log("InstallShadowFilterDecalPlugin: registered with Decal");
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
            Registry.CurrentUser.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
            var destDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                "Decal Plugins",
                FilterName);
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, recursive: true);
            session.Log("UninstallShadowFilterDecalPlugin: complete");
        }
        catch (Exception ex)
        {
            session.Log($"UninstallShadowFilterDecalPlugin: failed — {ex.Message}");
        }

        return ActionResult.Success;
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
