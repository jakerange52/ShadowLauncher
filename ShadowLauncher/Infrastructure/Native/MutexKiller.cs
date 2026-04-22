using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Closes a named mutex in a target process, allowing multiple instances of acclient.exe to run.
/// The AC game client creates a mutex to prevent multi-boxing; this removes it.
///
/// Implementation overview:
///   1. NtQuerySystemInformation(SystemHandleInformation) enumerates every open handle on the system.
///   2. Handles belonging to the target PID are collected.
///   3. Each handle is duplicated into our own process (DuplicateHandle) so we can query its name.
///   4. If the name contains "acclient", the original handle is closed in the source process
///      via DuplicateHandle with DUPLICATE_CLOSE_SOURCE, which removes the mutex.
/// </summary>
/// <remarks>
/// This class becomes a no-op once every acclient instance is launched from its own symlink
/// junction directory and therefore has its own mutex namespace. It is retained for compatibility
/// with servers that do not use the junction launch path.
/// </remarks>
internal static class MutexKiller
{
    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_CLOSE_SOURCE = 0x0001;
    private const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
    private const int SystemHandleInformation = 16;
    private const uint MUTEX_ALL_ACCESS = 0x001F0001;

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(
        IntPtr objectHandle,
        int objectInformationClass,
        IntPtr objectInformation,
        int objectInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemHandleEntry
    {
        public int OwnerProcessId;
        public byte ObjectTypeNumber;
        public byte Flags;
        public ushort HandleValue;
        public IntPtr ObjectPointer;
        public uint GrantedAccess;
    }

    /// <summary>
    /// Attempts to close all handles whose name contains <paramref name="mutexName"/> in the
    /// target process. Returns true if at least one handle was successfully closed.
    ///
    /// The technique relies on undocumented NtQuerySystemInformation behaviour that is
    /// available on all supported Windows versions but may require elevated handle access.
    /// </summary>
    public static bool CloseMutex(int processId, string mutexName = "acclient")
    {
        var processHandle = OpenProcess(PROCESS_DUP_HANDLE, false, processId);
        if (processHandle == IntPtr.Zero)
            return false;

        bool closed = false;
        try
        {
            var handles = GetProcessHandles(processId);
            var currentProcess = Process.GetCurrentProcess().Handle;

            foreach (var handle in handles)
            {
                IntPtr dupHandle = IntPtr.Zero;
                try
                {
                    if (!DuplicateHandle(processHandle, (IntPtr)handle.HandleValue,
                        currentProcess, out dupHandle, 0, false, 0x0002 /* DUPLICATE_SAME_ACCESS */))
                        continue;

                    var name = GetObjectName(dupHandle);
                    if (name != null && name.Contains(mutexName, StringComparison.OrdinalIgnoreCase))
                    {
                        CloseHandle(dupHandle);
                        dupHandle = IntPtr.Zero;

                        // Close the handle in the source process
                        DuplicateHandle(processHandle, (IntPtr)handle.HandleValue,
                            IntPtr.Zero, out _, 0, false, DUPLICATE_CLOSE_SOURCE);
                        closed = true;
                    }
                }
                catch
                {
                    // Skip problematic handles
                }
                finally
                {
                    if (dupHandle != IntPtr.Zero)
                        CloseHandle(dupHandle);
                }
            }

            return closed;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private static List<SystemHandleEntry> GetProcessHandles(int processId)
    {
        var result = new List<SystemHandleEntry>();
        var length = 0x10000;

        // NtQuerySystemInformation does not tell us the required buffer size up front;
        // we grow the buffer exponentially until it fits.
        while (true)
        {
            var ptr = Marshal.AllocHGlobal(length);
            try
            {
                var status = NtQuerySystemInformation(SystemHandleInformation, ptr, length, out var needed);
                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    length = needed + 0x10000; // add headroom to avoid a tight loop
                    continue;
                }
                if (status != 0)
                    return result;

                // Layout: int handleCount, then handleCount × SystemHandleEntry structs.
                var handleCount = Marshal.ReadInt32(ptr);
                var offset = IntPtr.Size; // skip count field (pointer-aligned on 64-bit)
                var entrySize = Marshal.SizeOf<SystemHandleEntry>();

                for (int i = 0; i < handleCount; i++)
                {
                    var entry = Marshal.PtrToStructure<SystemHandleEntry>(ptr + offset);
                    if (entry.OwnerProcessId == processId)
                        result.Add(entry);
                    offset += entrySize;
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    private static string? GetObjectName(IntPtr handle)
    {
        const int ObjectNameInformation = 1;

        // UNICODE_STRING layout: ushort Length, ushort MaxLength, then a pointer-aligned Buffer ptr.
        // We allocate enough for a reasonable path (~256 UTF-16 chars).
        var length = 0x200;
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            var status = NtQueryObject(handle, ObjectNameInformation, ptr, length, out var needed);
            if (status != 0)
                return null;

            // Read UNICODE_STRING: first field is the byte-length of the string data.
            var nameLength = Marshal.ReadInt16(ptr);
            if (nameLength <= 0) return null;
            // Buffer pointer follows Length + MaxLength (4 bytes) + alignment padding to pointer size.
            var buffer = Marshal.ReadIntPtr(ptr + IntPtr.Size);
            return Marshal.PtrToStringUni(buffer, nameLength / 2); // nameLength is in bytes; divide by 2 for chars
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
