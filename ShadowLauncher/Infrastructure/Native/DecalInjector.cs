using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Launches acclient.exe suspended, injects Decal's Inject.dll, then resumes the process.
/// This enables multi-client by loading Decal before any game code runs.
///
/// Inject.dll is located from the user-configured path in Settings first, then falls back
/// to auto-detection via the Decal registry key (HKLM\SOFTWARE\Decal\Agent → AgentPath).
/// </summary>
internal static class DecalInjector
{
    private const uint CREATE_SUSPENDED = 0x00000004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? applicationName, string commandLine,
        IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint creationFlags,
        IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    /// <summary>
    /// Launches <paramref name="exePath"/> suspended, injects <paramref name="decalInjectPath"/>,
    /// then resumes the process. Returns the process ID, or -1 on failure.
    /// </summary>
    public static int LaunchSuspendedAndInject(
        string exePath, string arguments, string workingDirectory, string decalInjectPath)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
        var commandLine = $"\"{exePath}\" {arguments}";

        if (!CreateProcess(null, commandLine, IntPtr.Zero, IntPtr.Zero,
            false, CREATE_SUSPENDED, IntPtr.Zero,
            string.IsNullOrEmpty(workingDirectory) ? null : workingDirectory,
            ref si, out var pi))
            return -1;

        try
        {
            Inject(pi.dwProcessId, decalInjectPath);
            ResumeThread(pi.hThread);
            return pi.dwProcessId;
        }
        catch
        {
            // If injection fails, resume anyway so we don't leave a zombie process.
            ResumeThread(pi.hThread);
            throw;
        }
        finally
        {
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
        }
    }

    private const uint PROCESS_ALL_ACCESS        = 0x1F0FFF;
    private const uint MEM_COMMIT_RESERVE        = 0x3000;
    private const uint PAGE_READWRITE            = 0x04;
    private const uint MEM_RELEASE               = 0x8000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr process, IntPtr address, nint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr process, IntPtr baseAddress,
        byte[] buffer, nint size, out nint written);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(
        IntPtr process, IntPtr threadAttributes, nint stackSize,
        IntPtr startAddress, IntPtr parameter,
        uint creationFlags, out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcessModulesEx(
        IntPtr process, IntPtr[] modules, uint size, out uint needed, uint filterFlag);

    [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetModuleBaseName(
        IntPtr process, IntPtr module,
        System.Text.StringBuilder baseName, uint size);

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the path to Decal's Inject.dll.
    /// Priority: user-configured path in Settings → Decal registry key.
    /// Returns null if Decal is not found.
    /// </summary>
    public static string? ResolveDecalInjectPath(string? configuredPath)
    {
        // 1. User explicitly configured a path.
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        // 2. Auto-detect from Decal registry key.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Decal\Agent");
            if (key is null) return null;

            var agentPath = key.GetValue("AgentPath") as string;
            if (string.IsNullOrWhiteSpace(agentPath)) return null;

            var injectDll = Path.Combine(agentPath, "Inject.dll");
            return File.Exists(injectDll) ? injectDll : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Injects <paramref name="dllPath"/> into the process identified by
    /// <paramref name="processId"/> using LoadLibraryW + DecalStartup via CreateRemoteThread.
    /// Works on suspended processes — CreateRemoteThread runs independently of the main thread.
    /// </summary>
    public static bool Inject(int processId, string dllPath)
    {
        var fullPath = Path.GetFullPath(dllPath);
        var pathBytes = System.Text.Encoding.Unicode.GetBytes(fullPath + "\0");

        var process = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
        if (process == IntPtr.Zero)
            throw new InvalidOperationException(
                $"OpenProcess failed for PID {processId}: {Marshal.GetLastWin32Error()}");

        IntPtr remoteBuffer = IntPtr.Zero;
        IntPtr remoteThread = IntPtr.Zero;

        try
        {
            // Allocate memory in the target process for the DLL path string.
            remoteBuffer = VirtualAllocEx(process, IntPtr.Zero, pathBytes.Length,
                MEM_COMMIT_RESERVE, PAGE_READWRITE);
            if (remoteBuffer == IntPtr.Zero)
                throw new InvalidOperationException($"VirtualAllocEx failed: {Marshal.GetLastWin32Error()}");

            if (!WriteProcessMemory(process, remoteBuffer, pathBytes, pathBytes.Length, out _))
                throw new InvalidOperationException($"WriteProcessMemory failed: {Marshal.GetLastWin32Error()}");

            // Step 1: LoadLibraryW — loads Inject.dll into the target process.
            var loadLibrary = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
                throw new InvalidOperationException("Could not resolve LoadLibraryW");

            remoteThread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remoteBuffer, 0, out _);
            if (remoteThread == IntPtr.Zero)
                throw new InvalidOperationException($"CreateRemoteThread(LoadLibraryW) failed: {Marshal.GetLastWin32Error()}");

            // Wait for LoadLibrary to finish.
            WaitForSingleObject(remoteThread, 10_000);
            CloseHandle(remoteThread);
            remoteThread = IntPtr.Zero;

            // Step 2: call DecalStartup — Decal's Inject.dll requires this export to be
            // explicitly invoked after loading to complete its initialisation.
            var decalStartup = GetRemoteProcAddress(process, fullPath, "DecalStartup");
            if (decalStartup != IntPtr.Zero)
            {
                remoteThread = CreateRemoteThread(process, IntPtr.Zero, 0, decalStartup, IntPtr.Zero, 0, out _);
                if (remoteThread != IntPtr.Zero)
                    WaitForSingleObject(remoteThread, 10_000);
            }

            return true;
        }
        finally
        {
            if (remoteBuffer != IntPtr.Zero) VirtualFreeEx(process, remoteBuffer, 0, MEM_RELEASE);
            if (remoteThread != IntPtr.Zero) CloseHandle(remoteThread);
            CloseHandle(process);
        }
    }

    /// <summary>
    /// Resolves the address of an exported function in a DLL that is loaded in a remote process.
    /// We load the DLL locally to calculate the export's RVA, then add the remote base address.
    /// </summary>
    private static IntPtr GetRemoteProcAddress(IntPtr process, string dllPath, string exportName)
    {
        // Load a local copy to find the export's offset from the DLL base.
        var localModule = LoadLibraryEx(dllPath, IntPtr.Zero, 0x00000001 /* DONT_RESOLVE_DLL_REFERENCES */);
        if (localModule == IntPtr.Zero) return IntPtr.Zero;

        try
        {
            var localExport = GetProcAddress(localModule, exportName);
            if (localExport == IntPtr.Zero) return IntPtr.Zero;

            // RVA = local export address - local module base
            long rva = localExport.ToInt64() - localModule.ToInt64();

            // Find the base address of the DLL in the remote process using the module name.
            var remoteBase = GetRemoteModuleBase(process, Path.GetFileName(dllPath));
            if (remoteBase == IntPtr.Zero) return IntPtr.Zero;

            return new IntPtr(remoteBase.ToInt64() + rva);
        }
        finally
        {
            FreeLibrary(localModule);
        }
    }

    /// <summary>
    /// Finds the base address of a loaded module in a remote process by name.
    /// </summary>
    private static IntPtr GetRemoteModuleBase(IntPtr process, string moduleName)
    {
        var modules = new IntPtr[1024];
        if (!EnumProcessModulesEx(process, modules, (uint)(modules.Length * IntPtr.Size), out var needed, 3))
            return IntPtr.Zero;

        int count = (int)(needed / IntPtr.Size);
        var nameBuffer = new System.Text.StringBuilder(256);

        for (int i = 0; i < count; i++)
        {
            nameBuffer.Clear();
            if (GetModuleBaseName(process, modules[i], nameBuffer, (uint)nameBuffer.Capacity) > 0)
            {
                if (string.Equals(nameBuffer.ToString(), moduleName, StringComparison.OrdinalIgnoreCase))
                    return modules[i];
            }
        }
        return IntPtr.Zero;
    }
}
