using System.Runtime.InteropServices;
using System.Text;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Win32 WINDOWPLACEMENT capture/restore for per-account game window layout (Thwarg parity).
/// </summary>
public static class GameWindowPlacementHelper
{
    private const int SwShowminimized = 2;
    private const int SwShownormal = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        public int length;
        public int flags;
        public int showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public Rect rcNormalPosition;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint hWnd, ref WindowPlacement lpwndpl);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint hWnd, ref WindowPlacement lpwndpl);

    internal static string? GetPlacementString(nint hwnd)
    {
        if (hwnd == nint.Zero)
            return null;

        var placement = new WindowPlacement { length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref placement))
            return null;

        if (placement.length <= 0)
            return null;

        return ToPlacementString(placement);
    }

    internal static bool TrySetPlacement(nint hwnd, string placementString)
    {
        if (hwnd == nint.Zero || string.IsNullOrWhiteSpace(placementString))
            return false;

        var placement = FromPlacementString(placementString);
        if (placement.length <= 0)
            return false;

        var current = new WindowPlacement { length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref current))
            return false;

        if (!AreSameNormalSize(placement, current))
            return false;

        placement.length = Marshal.SizeOf<WindowPlacement>();
        return SetWindowPlacement(hwnd, ref placement);
    }

    internal static bool AreSameNormalSize(WindowPlacement a, WindowPlacement b) =>
        GetNormalHeight(a) == GetNormalHeight(b) && GetNormalWidth(a) == GetNormalWidth(b);

    internal static int GetNormalHeight(WindowPlacement placement) =>
        placement.rcNormalPosition.Bottom - placement.rcNormalPosition.Top;

    internal static int GetNormalWidth(WindowPlacement placement) =>
        placement.rcNormalPosition.Right - placement.rcNormalPosition.Left;

    internal static bool IsEmpty(string? placementString)
    {
        if (string.IsNullOrWhiteSpace(placementString))
            return true;

        var placement = FromPlacementString(placementString);
        return placement.length <= 0;
    }

    internal static string GetSessionKey(string serverName, string accountName) =>
        $"s:{serverName}-a:{accountName}";

    private static string ToPlacementString(WindowPlacement placement)
    {
        var sb = new StringBuilder();
        sb.Append(placement.length).Append(',');
        sb.Append(placement.flags).Append(',');
        sb.Append(placement.showCmd).Append(',');
        sb.Append(placement.ptMinPosition.X).Append(',');
        sb.Append(placement.ptMinPosition.Y).Append(',');
        sb.Append(placement.ptMaxPosition.X).Append(',');
        sb.Append(placement.ptMaxPosition.Y).Append(',');
        sb.Append(placement.rcNormalPosition.Left).Append(',');
        sb.Append(placement.rcNormalPosition.Top).Append(',');
        sb.Append(placement.rcNormalPosition.Right).Append(',');
        sb.Append(placement.rcNormalPosition.Bottom);
        return sb.ToString();
    }

    private static WindowPlacement FromPlacementString(string placementString)
    {
        var parts = placementString.Split(',');
        if (parts.Length < 11)
            return default;

        if (!int.TryParse(parts[0], out var length) || length <= 0)
            return default;

        return new WindowPlacement
        {
            length = length,
            flags = int.Parse(parts[1]),
            showCmd = int.Parse(parts[2]),
            ptMinPosition = new Point { X = int.Parse(parts[3]), Y = int.Parse(parts[4]) },
            ptMaxPosition = new Point { X = int.Parse(parts[5]), Y = int.Parse(parts[6]) },
            rcNormalPosition = new Rect
            {
                Left = int.Parse(parts[7]),
                Top = int.Parse(parts[8]),
                Right = int.Parse(parts[9]),
                Bottom = int.Parse(parts[10])
            }
        };
    }
}
