using Microsoft.Win32;

namespace ShadowLauncher.Infrastructure.Decal;

/// <summary>
/// Decal network-filter registration for ShadowFilter.
/// The GUID matches <c>ShadowFilterPluginIds.AssemblyGuid</c>.
/// ShadowFilter takes auto-login from ThwargFilter only when both are true:
/// the filter is registered (HKLM, and ShadowFilter.dll exists at that path)
/// and the user checkbox is on (HKCU Enabled).
/// </summary>
public static class ShadowFilterRegistration
{
    public const string AssemblyGuid = "A8F3C2D1-4E5B-6A7C-8D9E-0F1A2B3C4D5E";
    private const string AssemblyFileName = "ShadowFilter.dll";

    private static readonly string SubKey =
        $@"Software\Decal\NetworkFilters\{{{AssemblyGuid}}}";

    public readonly record struct Status(bool Registered, bool Enabled, string DllPath)
    {
        public bool TakesOver => Registered && Enabled;
    }

    private static Status? _atStartup;

    /// <summary>
    /// Result captured when the launcher started. Later launch-file and command
    /// writes use this instead of reading Decal again.
    /// </summary>
    public static Status Current => _atStartup ?? Query();

    public static bool IsEnabled() => Current.TakesOver;

    public static Status CaptureAtStartup()
    {
        var status = Query();
        _atStartup = status;
        return status;
    }

    public static Status Query()
    {
        var registered = false;
        var enabled = false;
        var dllPath = string.Empty;

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(SubKey);
                if (key is null)
                    continue;

                var assemblyName = key.GetValue("Assembly") as string;
                var folder = key.GetValue("Path") as string;
                if (string.IsNullOrWhiteSpace(assemblyName) || string.IsNullOrWhiteSpace(folder))
                    continue;
                if (!string.Equals(assemblyName, AssemblyFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidate = Path.Combine(folder, assemblyName);
                if (!File.Exists(candidate))
                    continue;

                registered = true;
                dllPath = candidate;
                break;
            }
            catch (Exception)
            {
            }
        }

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = user.OpenSubKey(SubKey);
                if (key?.GetValue("Enabled") is int flag && flag != 0)
                {
                    enabled = true;
                    break;
                }
            }
            catch (Exception)
            {
            }
        }

        return new Status(registered, enabled, dllPath);
    }
}
