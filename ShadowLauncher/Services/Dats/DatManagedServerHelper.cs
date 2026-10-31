using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Dats;

/// <summary>
/// Identifies servers whose DAT files are managed by the launcher cache (registry or zip),
/// matching the Server Details "Refresh DATs" visibility rules.
/// </summary>
public static class DatManagedServerHelper
{
    public static bool IsRetailDatSetId(string? datSetId)
        => string.IsNullOrWhiteSpace(datSetId)
           || string.Equals(datSetId, "retail", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the launcher owns download/cache for this server's DAT set.
    /// Excludes local <see cref="Server.CustomDatRegistryPath"/> folders.
    /// </summary>
    public static bool UsesManagedDatSet(Server server)
    {
        if (!string.IsNullOrWhiteSpace(server.CustomDatRegistryPath))
            return false;

        if (!string.IsNullOrWhiteSpace(server.CustomDatZipUrl))
            return true;

        return !IsRetailDatSetId(server.DatSetId);
    }
}
