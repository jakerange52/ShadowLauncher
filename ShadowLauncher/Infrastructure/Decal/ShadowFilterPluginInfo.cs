namespace ShadowLauncher.Infrastructure.Decal;

/// <summary>
/// Shared constants for ShadowFilter Decal plugin registration.
/// </summary>
public static class ShadowFilterPluginInfo
{
    public const string FilterName = "ShadowFilter";
    public const string AssemblyGuid = "A8F3C2D1-4E5B-6A7C-8D9E-0F1A2B3C4D5E";
    public const string RegistryKeyPath = @"Software\Decal\Plugins\" + AssemblyGuid;
}
