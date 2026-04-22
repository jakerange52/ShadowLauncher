using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Sends mouse clicks to the AC game window to skip intro movies.
/// Uses PostMessage so the window doesn't need to be in the foreground.
/// </summary>
internal static class MovieSkipper
{
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    /// <summary>
    /// Sends clicks to the game window at intervals to skip intro movies.
    /// Runs on a background thread and stops after maxAttempts.
    /// </summary>
    public static void StartSkipping(int processId, int intervalMs = 1500, int maxAttempts = 8)
    {
        _ = Task.Run(async () =>
        {
            // Wait a moment for the window to appear
            await Task.Delay(2000);

            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    var proc = Process.GetProcessById(processId);
                    if (proc.HasExited) break;

                    var hWnd = FindWindowForProcess(processId);
                    if (hWnd != nint.Zero)
                    {
                        // Click at center-ish area (350, 100) — same coords ThwargFilter uses
                        nint lParam = MakeLParam(350, 100);
                        PostMessage(hWnd, WM_LBUTTONDOWN, 1, lParam);
                        PostMessage(hWnd, WM_LBUTTONUP, 0, lParam);
                    }
                }
                catch (ArgumentException)
                {
                    break; // Process exited
                }

                await Task.Delay(intervalMs);
            }
        });
    }

    private static nint FindWindowForProcess(int processId)
    {
        nint found = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == processId && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false; // stop enumerating
            }
            return true;
        }, nint.Zero);
        return found;
    }

    private static nint MakeLParam(int x, int y) => (nint)((y << 16) | (x & 0xFFFF));
}
