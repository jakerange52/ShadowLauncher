using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShadowFilter;

/// <summary>
/// acclient is 32-bit — the Decal CLR shares that ~2 GB VA. An OOM from VTank,
/// UtilityBelt, or us usually leaves a hung client (modal dialog or a thread
/// that never runs again) rather than a clean exit. Kill immediately; the
/// launcher's heartbeat timeout is the fallback when this path cannot allocate.
/// </summary>
internal static class OomGuard
{
    private const int ErrorOutOfMemory = unchecked((int)0x8007000E);
    private const int E_OutOfMemory = unchecked((int)0x80000002);

    // Freed on the kill path so TerminateProcess can run if the allocator is dead.
    private static byte[]? _reserve = new byte[256 * 1024];
    private static Process? _self;
    private static int _killing;

    public static void Install()
    {
        _self = Process.GetCurrentProcess();
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
    }

    public static void DieIfOom(Exception? ex)
    {
        if (IsOutOfMemory(ex))
            Kill(ex!);
    }

    public static void Kill(Exception ex) => Kill(ex.GetType().Name);

    public static void Kill(string reason)
    {
        if (Interlocked.Exchange(ref _killing, 1) != 0)
            return;

        _reserve = null;

        try
        {
            PluginLog.Error(nameof(OomGuard), $"Out of memory — killing acclient ({reason})");
        }
        catch
        {
            // Logging may itself fail to allocate.
        }

        try
        {
            _self?.Kill();
        }
        catch
        {
            try
            {
                Environment.FailFast("ShadowFilter: " + reason);
            }
            catch
            {
                // Last resort — if this throws the process is already dying.
            }
        }
    }

    private static void OnUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        DieIfOom(e.ExceptionObject as Exception);
    }

    private static bool IsOutOfMemory(Exception? ex)
    {
        while (ex != null)
        {
            if (ex is OutOfMemoryException)
                return true;

            if (ex is COMException com
                && (com.ErrorCode == ErrorOutOfMemory || com.ErrorCode == E_OutOfMemory))
                return true;

            ex = ex.InnerException;
        }

        return false;
    }
}
