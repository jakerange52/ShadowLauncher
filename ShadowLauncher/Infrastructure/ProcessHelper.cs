namespace ShadowLauncher.Infrastructure;

/// <summary>Shared process liveness checks used by monitoring and channel relay.</summary>
internal static class ProcessHelper
{
    public static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
