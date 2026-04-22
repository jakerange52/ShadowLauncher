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

    private const int SW_RESTORE = 9;

    /// <summary>
    /// Brings the main window of the given process to the foreground.
    /// Returns true if a window was found and focused.
    /// </summary>
    public static bool FocusProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            var hWnd = process.MainWindowHandle;

            if (hWnd == IntPtr.Zero)
                return false;

            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);

            return SetForegroundWindow(hWnd);
        }
        catch (ArgumentException)
        {
            // Process no longer exists
            return false;
        }
    }
}
