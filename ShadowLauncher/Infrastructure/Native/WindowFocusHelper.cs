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

    private const int SW_RESTORE  = 9;
    private const int SW_MINIMIZE = 6;

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

    /// <summary>Minimizes the main window of the given process.</summary>
    public static bool MinimizeProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var hWnd = process.MainWindowHandle;
            if (hWnd == IntPtr.Zero) return false;
            ShowWindow(hWnd, SW_MINIMIZE);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Restores the main window of the given process if minimized.</summary>
    public static bool RestoreProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var hWnd = process.MainWindowHandle;
            if (hWnd == IntPtr.Zero) return false;
            ShowWindow(hWnd, SW_RESTORE);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Returns true if the main window of the given process is minimized.</summary>
    public static bool IsMinimized(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            var hWnd = process.MainWindowHandle;
            return hWnd != IntPtr.Zero && IsIconic(hWnd);
        }
        catch (ArgumentException) { return false; }
    }
}
