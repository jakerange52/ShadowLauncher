using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShadowLauncher.Infrastructure.Native;

internal static partial class WindowFocusHelper
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private const int SW_RESTORE  = 9;
    private const int SW_MINIMIZE = 6;

    /// <summary>
    /// Finds the first visible top-level window belonging to <paramref name="processId"/>.
    /// Returns <see cref="nint.Zero"/> if none is found.
    /// </summary>
    internal static nint FindWindowForProcess(int processId)
    {
        nint found = nint.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd))
            {
                found = hWnd;
                return false; // stop enumerating
            }
            return true;
        }, nint.Zero);
        return found;
    }

    /// <summary>Brings the main window of the given process to the foreground.
    /// Returns true if a window was found and focused.
    /// </summary>
    public static bool FocusProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var hWnd = process.MainWindowHandle;

            if (hWnd == IntPtr.Zero)
                return false;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            return SetForegroundWindow(hWnd);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Tries to read the minimized state of the process's main window.
    /// Returns false (out param undefined) when the process has no main window
    /// — callers should treat that as "unknown" and not overwrite a previously
    /// captured value.
    /// </summary>
    public static bool TryGetMinimizedState(int processId, out bool isMinimized)
    {
        isMinimized = false;
        try
        {
            using var process = Process.GetProcessById(processId);
            var hWnd = process.MainWindowHandle;
            if (hWnd == IntPtr.Zero) return false;
            isMinimized = IsIconic(hWnd);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// Minimizes every visible top-level window owned by the given process.
    /// Returns the number of windows minimized.
    /// </summary>
    public static int MinimizeAllWindows(int processId)
    {
        int count = 0;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == (uint)processId && IsWindowVisible(hWnd) && !IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_MINIMIZE);
                count++;
            }
            return true;
        }, nint.Zero);
        return count;
    }
}
