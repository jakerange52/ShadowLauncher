using System.Runtime.InteropServices;
using System.Security.Principal;
using WixToolset.Dtf.WindowsInstaller;

namespace ShadowLauncher.Installer.CustomActions;

/// <summary>
/// WiX DTF custom actions for ShadowLauncher install/uninstall.
/// </summary>
public static class PrivilegeActions
{
    /// <summary>
    /// Deletes %LOCALAPPDATA%\ShadowLauncher on uninstall (DAT cache, settings, logs).
    /// Runs impersonated as the installing user so the correct per-user LocalAppData is targeted.
    /// </summary>
    [CustomAction]
    public static ActionResult CleanupAppData(Session session)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, "ShadowLauncher");
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
                session.Log($"CleanupAppData: deleted {dir}");
            }
            else
            {
                session.Log($"CleanupAppData: nothing to delete at {dir}");
            }
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"CleanupAppData: failed — {ex.Message}");
            return ActionResult.Success; // non-fatal — don't block uninstall
        }
    }


    /// <summary>
    /// Immediate (non-elevated) check that runs before <see cref="GrantSymlinkPrivilege"/>.
    /// If BUILTIN\Users does not already hold SeCreateSymbolicLinkPrivilege, sets MSI
    /// property SHOWLOGOFFMESSAGE=1 so the ExitDialog can tell the user they need to
    /// sign out and back in for the new privilege to take effect.
    /// </summary>
    [CustomAction]
    public static ActionResult CheckSymlinkPrivilege(Session session)
    {
        try
        {
            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            byte[] sidBytes = new byte[usersSid.BinaryLength];
            usersSid.GetBinaryForm(sidBytes, 0);

            var objectAttributes = new LsaObjectAttributes { Length = (uint)Marshal.SizeOf<LsaObjectAttributes>() };
            uint status = LsaOpenPolicy(IntPtr.Zero, ref objectAttributes, PolicyAccess.LookupNames, out var policyHandle);
            if (status != 0)
            {
                session.Log($"CheckSymlinkPrivilege: LsaOpenPolicy failed NTSTATUS=0x{status:X8} — assuming privilege not present");
                session["SHOWLOGOFFMESSAGE"] = "1";
                return ActionResult.Success;
            }

            try
            {
                var sidHandle = GCHandle.Alloc(sidBytes, GCHandleType.Pinned);
                try
                {
                    status = LsaEnumerateAccountRights(policyHandle, sidHandle.AddrOfPinnedObject(), out var rightsPtr, out var rightsCount);
                    if (status != 0 || rightsPtr == IntPtr.Zero)
                    {
                        // STATUS_OBJECT_NAME_NOT_FOUND (0xC0000034) means no rights are assigned to the SID.
                        session.Log($"CheckSymlinkPrivilege: account has no rights yet (NTSTATUS=0x{status:X8}) — will need logoff");
                        session["SHOWLOGOFFMESSAGE"] = "1";
                        return ActionResult.Success;
                    }

                    try
                    {
                        bool found = false;
                        int structSize = Marshal.SizeOf<LsaUnicodeString>();
                        for (int i = 0; i < rightsCount; i++)
                        {
                            var entry = Marshal.PtrToStructure<LsaUnicodeString>(IntPtr.Add(rightsPtr, i * structSize));
                            if (string.Equals(entry.Buffer, "SeCreateSymbolicLinkPrivilege", StringComparison.Ordinal))
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            session.Log("CheckSymlinkPrivilege: SeCreateSymbolicLinkPrivilege not currently granted — will show logoff message");
                            session["SHOWLOGOFFMESSAGE"] = "1";
                        }
                        else
                        {
                            session.Log("CheckSymlinkPrivilege: SeCreateSymbolicLinkPrivilege already granted — no logoff message needed");
                        }
                    }
                    finally
                    {
                        LsaFreeMemory(rightsPtr);
                    }
                }
                finally
                {
                    sidHandle.Free();
                }
            }
            finally
            {
                LsaClose(policyHandle);
            }

            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"CheckSymlinkPrivilege: exception — {ex.Message}; defaulting to show logoff message");
            session["SHOWLOGOFFMESSAGE"] = "1";
            return ActionResult.Success;
        }
    }

    [CustomAction]
    public static ActionResult GrantSymlinkPrivilege(Session session)
    {
        session.Log("GrantSymlinkPrivilege: granting SeCreateSymbolicLinkPrivilege to BUILTIN\\Users");

        try
        {
            // Resolve the BUILTIN\Users SID (S-1-5-32-545).
            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            byte[] sidBytes = new byte[usersSid.BinaryLength];
            usersSid.GetBinaryForm(sidBytes, 0);

            var objectAttributes = new LsaObjectAttributes { Length = (uint)Marshal.SizeOf<LsaObjectAttributes>() };
            uint status = LsaOpenPolicy(
                IntPtr.Zero,
                ref objectAttributes,
                PolicyAccess.CreateAccount | PolicyAccess.LookupNames,
                out var policyHandle);

            if (status != 0)
            {
                session.Log($"GrantSymlinkPrivilege: LsaOpenPolicy failed NTSTATUS=0x{status:X8}");
                return ActionResult.Failure;
            }

            try
            {
                var sidHandle = GCHandle.Alloc(sidBytes, GCHandleType.Pinned);
                try
                {
                    var rights = new[] { new LsaUnicodeString("SeCreateSymbolicLinkPrivilege") };
                    status = LsaAddAccountRights(policyHandle, sidHandle.AddrOfPinnedObject(), rights, (uint)rights.Length);

                    if (status != 0)
                    {
                        session.Log($"GrantSymlinkPrivilege: LsaAddAccountRights failed NTSTATUS=0x{status:X8}");
                        return ActionResult.Failure;
                    }
                }
                finally
                {
                    sidHandle.Free();
                }
            }
            finally
            {
                LsaClose(policyHandle);
            }

            session.Log("GrantSymlinkPrivilege: privilege granted successfully");
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            session.Log($"GrantSymlinkPrivilege: exception — {ex.Message}");
            return ActionResult.Failure;
        }
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [Flags]
    private enum PolicyAccess : uint
    {
        LookupNames   = 0x00000800,
        CreateAccount = 0x00000010,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LsaObjectAttributes
    {
        public uint   Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint   Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LsaUnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Buffer;

        public LsaUnicodeString(string s)
        {
            Buffer        = s;
            Length        = (ushort)(s.Length * 2);
            MaximumLength = (ushort)(Length + 2);
        }
    }

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaOpenPolicy(
        IntPtr                  systemName,
        ref LsaObjectAttributes objectAttributes,
        PolicyAccess            desiredAccess,
        out IntPtr              policyHandle);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaAddAccountRights(
        IntPtr             policyHandle,
        IntPtr             accountSid,
        LsaUnicodeString[] userRights,
        uint               countOfRights);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaEnumerateAccountRights(
        IntPtr     policyHandle,
        IntPtr     accountSid,
        out IntPtr userRights,
        out uint   countOfRights);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaFreeMemory(IntPtr buffer);

    [DllImport("advapi32.dll", SetLastError = false)]
    private static extern uint LsaClose(IntPtr objectHandle);
}
