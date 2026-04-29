using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Renames the game window title to "Account - ServerName" after launch,
/// making it easy to identify individual instances in the taskbar.
/// </summary>
internal static class WindowTitleSetter
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(nint hWnd, string lpString);

    /// <summary>
    /// Waits for the game window to appear then sets its title to "{accountName} - {serverName}".
    /// Runs on a background thread — fire and forget.
    /// </summary>
    public static void SetTitleAsync(int processId, string accountName, string serverName)
    {
        _ = Task.Run(async () =>
        {
            var title = $"{accountName} - {serverName}";

            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(1500);

                try
                {
                    using var proc = Process.GetProcessById(processId);
                    if (proc.HasExited) return;
                }
                catch (ArgumentException) { return; }

                var hWnd = WindowFocusHelper.FindWindowForProcess(processId);
                if (hWnd != nint.Zero)
                {
                    SetWindowText(hWnd, title);
                    return;
                }
            }
        });
    }
}
