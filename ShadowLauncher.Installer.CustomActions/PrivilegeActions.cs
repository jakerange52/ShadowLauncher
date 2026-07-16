using WixToolset.Dtf.WindowsInstaller;

namespace ShadowLauncher.Installer.CustomActions;

/// <summary>
/// WiX DTF custom actions for ShadowLauncher install/uninstall.
/// </summary>
public static class PrivilegeActions
{
    /// <summary>
    /// Removes ephemeral runtime folders under %LOCALAPPDATA%\ShadowLauncher on uninstall.
    /// Preserves DatSets, settings, accounts, and other user data so reinstalls stay painless.
    /// Runs impersonated as the installing user so the correct per-user LocalAppData is targeted.
    /// </summary>
    [CustomAction]
    public static ActionResult CleanupAppData(Session session)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(localAppData, "ShadowLauncher");
            if (!Directory.Exists(root))
            {
                session.Log($"CleanupAppData: nothing to delete at {root}");
                return ActionResult.Success;
            }

            // Ephemeral only — hard-link instance trees, launch IPC, heartbeats, logs.
            foreach (var name in new[] { "Instances", "LaunchFiles", "Running", "Logs" })
            {
                var dir = Path.Combine(root, name);
                if (!Directory.Exists(dir))
                    continue;

                try
                {
                    Directory.Delete(dir, recursive: true);
                    session.Log($"CleanupAppData: deleted {dir}");
                }
                catch (Exception ex)
                {
                    session.Log($"CleanupAppData: failed to delete {dir} — {ex.Message}");
                }
            }

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"CleanupAppData: failed — {ex.Message}");
            return ActionResult.Success; // non-fatal — don't block uninstall
        }
    }
}
