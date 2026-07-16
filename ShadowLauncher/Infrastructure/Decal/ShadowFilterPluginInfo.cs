namespace ShadowLauncher.Infrastructure.Decal;

/// <summary>
/// Shared constants for ShadowFilter Decal network-filter registration.
/// Shape matches ThwargFilter / Mag-Filter / UtilityBelt under HKLM NetworkFilters.
/// </summary>
public static class ShadowFilterPluginInfo
{
    public const string FilterName = "ShadowFilter";
    public const string AssemblyFileName = "ShadowFilter.dll";
    public const string ObjectTypeName = "ShadowFilter.FilterCore";
    public const string AssemblyGuid = "A8F3C2D1-4E5B-6A7C-8D9E-0F1A2B3C4D5E";

    /// <summary>Decal.Adapter Surrogate — loads .NET FilterBase types into the client.</summary>
    public const string SurrogateGuid = "{71A69713-6593-47EC-0002-0000000DECA1}";

    public const string NetworkFiltersKeyPath = @"Software\Decal\NetworkFilters\" + AssemblyGuid;

    /// <summary>Legacy key written by earlier installer builds (wrong for FilterBase).</summary>
    public const string LegacyPluginsKeyPath = @"Software\Decal\Plugins\" + AssemblyGuid;
}
