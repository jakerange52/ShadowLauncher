namespace ShadowLauncher.Infrastructure;

/// <summary>Shared process liveness checks used by monitoring and channel relay.</summary>
internal static class ProcessHelper
{
    private const string AcClientProcessName = "acclient";

    /// <summary>True when <paramref name="processId"/> is a live acclient.exe (not PID reuse).</summary>
    public static bool IsAcClientRunning(int processId)
    {
        if (processId <= 0)
            return false;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            if (!string.Equals(process.ProcessName, AcClientProcessName, StringComparison.OrdinalIgnoreCase))
                return false;

            try
            {
                return !process.HasExited;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Process exists but exit status is restricted — still treat as running acclient.
                return true;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static bool IsRunning(int processId) => IsAcClientRunning(processId);
}
