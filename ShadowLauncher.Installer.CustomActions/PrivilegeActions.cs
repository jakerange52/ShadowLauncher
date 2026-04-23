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

            var objectAttributes = new LsaObjectAttributes();
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
    private static extern uint LsaClose(IntPtr objectHandle);
}
