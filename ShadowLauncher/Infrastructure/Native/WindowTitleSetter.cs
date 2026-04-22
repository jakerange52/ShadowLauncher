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

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    /// <summary>
    /// Waits for the game window to appear then sets its title to "{accountName} - {serverName}".
    /// Runs on a background thread — fire and forget.
    /// </summary>
    public static void SetTitleAsync(int processId, string accountName, string serverName)
    {
        _ = Task.Run(async () =>
        {
            var title = $"{accountName} - {serverName}";

            // Poll until the window is visible (can take several seconds during AC startup).
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(1500);

                try
                {
                    var proc = Process.GetProcessById(processId);
                    if (proc.HasExited) return;
                }
                catch (ArgumentException) { return; }

                var hWnd = FindWindowForProcess(processId);
                if (hWnd != nint.Zero)
                {
                    SetWindowText(hWnd, title);
                    return;
                }
            }
        });
    }

    private static nint FindWindowForProcess(int processId)
    {
        nint found = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false;
            }
            return true;
        }, nint.Zero);
        return found;
    }
}
