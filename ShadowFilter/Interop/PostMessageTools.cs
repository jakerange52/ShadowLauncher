using System.Diagnostics;
using System.Runtime.InteropServices;
using Decal.Adapter;

namespace ShadowFilter.Interop;

internal static class PostMessageTools
{
    private const byte VkReturn = 0x0D;

    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;

    public static void SendEnter()
    {
        var hwnd = GetTargetHwnd();
        if (hwnd == IntPtr.Zero)
            return;

        PostMessage(hwnd, WmKeyDown, (IntPtr)VkReturn, (IntPtr)0x001C0001);
        PostMessage(hwnd, WmKeyUp, (IntPtr)VkReturn, (IntPtr)0xC01C0001);
    }

    public static void SendMsg(string msg)
    {
        foreach (var ch in msg)
            SendMsgKey(ch);
    }

    public static void ClickOK()
    {
        if (!GetWindowRect(GetTargetHwnd(), out var rect))
            return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        SendMouseClick(width / 2, height / 2 + 18);
        SendMouseClick(width / 2, height / 2 + 25);
        SendMouseClick(width / 2, height / 2 + 31);
    }

    public static void ClickYes()
    {
        if (!GetWindowRect(GetTargetHwnd(), out var rect))
            return;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        SendMouseClick(width / 2 - 80, height / 2 + 18);
        SendMouseClick(width / 2 - 80, height / 2 + 25);
        SendMouseClick(width / 2 - 80, height / 2 + 31);
    }

    public static void SendMouseClick(int x, int y)
    {
        var hwnd = GetTargetHwnd();
        if (hwnd == IntPtr.Zero)
            return;

        var lParam = MakeLParam(x, y);
        PostMessage(hwnd, WmMouseMove, IntPtr.Zero, lParam);
        PostMessage(hwnd, WmLButtonDown, (IntPtr)1, lParam);
        PostMessage(hwnd, WmLButtonUp, IntPtr.Zero, lParam);
    }

    private static void SendMsgKey(char ch)
    {
        var hwnd = GetTargetHwnd();
        if (hwnd == IntPtr.Zero)
            return;

        var scanCode = ScanCode(ch);
        var code = CharCode(ch);
        if (code == 0)
            return;

        var lParam = (uint)((scanCode << 16) | 1);
        PostMessage(hwnd, WmKeyDown, (IntPtr)code, (IntPtr)lParam);
        PostMessage(hwnd, WmKeyUp, (IntPtr)code, (IntPtr)(0xC0000000 | lParam));
    }

    private static byte ScanCode(char ch)
    {
        switch (char.ToLower(ch))
        {
            case 'a': return 0x1E;
            case 'b': return 0x30;
            case 'c': return 0x2E;
            case 'd': return 0x20;
            case 'e': return 0x12;
            case 'f': return 0x21;
            case 'g': return 0x22;
            case 'h': return 0x23;
            case 'i': return 0x17;
            case 'j': return 0x24;
            case 'k': return 0x25;
            case 'l': return 0x26;
            case 'm': return 0x32;
            case 'n': return 0x31;
            case 'o': return 0x18;
            case 'p': return 0x19;
            case 'q': return 0x10;
            case 'r': return 0x13;
            case 's': return 0x1F;
            case 't': return 0x14;
            case 'u': return 0x16;
            case 'v': return 0x2F;
            case 'w': return 0x11;
            case 'x': return 0x2D;
            case 'y': return 0x15;
            case 'z': return 0x2C;
            case '/': return 0x35;
            case ' ': return 0x39;
            default: return 0;
        }
    }

    private static byte CharCode(char ch)
    {
        switch (char.ToLower(ch))
        {
            case 'a': return 0x41;
            case 'b': return 0x42;
            case 'c': return 0x43;
            case 'd': return 0x44;
            case 'e': return 0x45;
            case 'f': return 0x46;
            case 'g': return 0x47;
            case 'h': return 0x48;
            case 'i': return 0x49;
            case 'j': return 0x4A;
            case 'k': return 0x4B;
            case 'l': return 0x4C;
            case 'm': return 0x4D;
            case 'n': return 0x4E;
            case 'o': return 0x4F;
            case 'p': return 0x50;
            case 'q': return 0x51;
            case 'r': return 0x52;
            case 's': return 0x53;
            case 't': return 0x54;
            case 'u': return 0x55;
            case 'v': return 0x56;
            case 'w': return 0x57;
            case 'x': return 0x58;
            case 'y': return 0x59;
            case 'z': return 0x5A;
            case '/': return 0xBF;
            case ' ': return 0x20;
            default: return 0;
        }
    }

    private static IntPtr GetTargetHwnd()
    {
        try
        {
            var decalHwnd = CoreManager.Current.Decal.Hwnd;
            if (decalHwnd != IntPtr.Zero)
                return decalHwnd;
        }
        catch { }

        return GetLargestVisibleProcessWindow();
    }

    private static IntPtr GetLargestVisibleProcessWindow()
    {
        var pid = Process.GetCurrentProcess().Id;
        IntPtr best = IntPtr.Zero;
        var bestArea = 0;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var windowPid);
            if (windowPid != (uint)pid || !IsWindowVisible(hWnd))
                return true;

            if (!GetWindowRect(hWnd, out var rect))
                return true;

            var area = Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = hWnd;
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    private static IntPtr MakeLParam(int x, int y) =>
        (IntPtr)((y << 16) | (x & 0xFFFF));

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
