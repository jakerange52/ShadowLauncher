using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace ShadowLauncher.Infrastructure.Native;

/// <summary>
/// Ensures SeCreateSymbolicLinkPrivilege is granted to BUILTIN\Users at runtime,
/// covering users who installed an older build before the installer granted it unconditionally.
/// </summary>
public static class SymlinkPrivilegeHelper
{
    public enum PrivilegeStatus
    {
        /// <summary>Privilege is active in the current token — symlinks will work.</summary>
        AlreadyActive,
        /// <summary>Privilege was just granted via LSA — user must sign out and back in.</summary>
        GrantedNeedsLogoff,
        /// <summary>Privilege grant failed (e.g. not elevated). User must run as admin once.</summary>
        GrantFailed,
    }

    /// <summary>
    /// Checks whether symlink creation is already working. If not, attempts to grant
    /// SeCreateSymbolicLinkPrivilege to BUILTIN\Users via the LSA policy API.
    /// Returns a <see cref="PrivilegeStatus"/> describing what happened.
    /// </summary>
    public static PrivilegeStatus EnsurePrivilege(ILogger? logger = null)
    {
        if (SymlinkLauncher.CanCreateSymlinks())
        {
            logger?.LogDebug("SymlinkPrivilegeHelper: symlinks already working");
            return PrivilegeStatus.AlreadyActive;
        }

        logger?.LogWarning("SymlinkPrivilegeHelper: symlinks not working — attempting to grant SeCreateSymbolicLinkPrivilege");

        try
        {
            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            byte[] sidBytes = new byte[usersSid.BinaryLength];
            usersSid.GetBinaryForm(sidBytes, 0);

            var objectAttributes = new LsaObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<LsaObjectAttributes>()
            };

            uint status = LsaOpenPolicy(
                IntPtr.Zero,
                ref objectAttributes,
                PolicyAccess.LookupNames | PolicyAccess.CreateAccount,
                out var policyHandle);

            if (status != 0)
            {
                logger?.LogWarning("SymlinkPrivilegeHelper: LsaOpenPolicy failed NTSTATUS=0x{Status:X8} — not elevated?", status);
                return PrivilegeStatus.GrantFailed;
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
                        logger?.LogWarning("SymlinkPrivilegeHelper: LsaAddAccountRights failed NTSTATUS=0x{Status:X8}", status);
                        return PrivilegeStatus.GrantFailed;
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

            logger?.LogInformation("SymlinkPrivilegeHelper: SeCreateSymbolicLinkPrivilege granted — logoff required");
            return PrivilegeStatus.GrantedNeedsLogoff;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SymlinkPrivilegeHelper: exception while granting privilege");
            return PrivilegeStatus.GrantFailed;
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
