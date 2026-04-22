using System.Runtime.InteropServices;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Launches the AC game client via injector.dll, which creates the process with injection
/// and bypasses the single-instance mutex entirely.
/// </summary>
/// <remarks>
/// Once every instance is launched from its own symlink junction directory the mutex is no
/// longer shared, so injection is no longer needed purely for multi-boxing. This class is
/// kept for Decal loading — injector.dll is the mechanism that injects Decal into the client.
/// </remarks>
internal static class InjectedLauncher
{
    /// <summary>
    /// P/Invoke binding to injector.dll's LaunchInjected export.
    /// Signature: LaunchInjected(commandLine, workingDir, dllToInject, initializeFunction)
    ///   - First three params are Unicode wide strings.
    ///   - Fourth param (initialize_function) is ANSI — the name of the DLL entry point to call.
    /// Returns the process ID of the launched acclient.exe, or &lt;= 0 on failure.
    /// </summary>
    [DllImport("injector.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int LaunchInjected(
        string command_line,
        string working_directory,
        string inject_dll_path,
        [MarshalAs(UnmanagedType.LPStr)] string initialize_function);

    /// <summary>
    /// Launches the game client with DLL injection, bypassing the mutex.
    /// Working directory is derived from the client exe's folder so relative
    /// assets (portal.dat, etc.) resolve correctly.
    /// </summary>
    public static int Launch(string clientPath, string arguments, string decalInjectPath)
    {
        var commandLine = $"\"{clientPath}\" {arguments}";
        var workingDir = Path.GetDirectoryName(clientPath) ?? string.Empty;

        return LaunchInjected(commandLine, workingDir, decalInjectPath, "DecalStartup");
    }
}
