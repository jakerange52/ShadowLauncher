using System.Runtime.InteropServices;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>Sets the explicit Application User Model ID for the current process.</summary>
/// <remarks>
/// The ID must match the AppUserModelId on the Start Menu shortcut so taskbar pins
/// survive upgrades (Windows tracks the pin by this stable ID).
/// </remarks>
public static class AppUserModelId
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void Set(string appId) => SetCurrentProcessExplicitAppUserModelID(appId);
}
