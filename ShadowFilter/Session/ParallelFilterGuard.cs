namespace ShadowFilter.Session;

/// <summary>
/// When ThwargFilter is loaded alongside ShadowFilter, ThwargFilter owns character-select
/// clicks (ShadowLauncher dual-writes its launch files). ShadowFilter still handles
/// heartbeats, character lists, and login commands under LocalAppData\ShadowLauncher.
/// </summary>
internal static class ParallelFilterGuard
{
    // Only cache a positive hit — ThwargFilter may load after ShadowFilter.Startup.
    private static bool _thwargFilterLoaded;

    public static bool IsThwargFilterLoaded()
    {
        if (_thwargFilterLoaded)
            return true;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(asm.GetName().Name, "ThwargFilter", StringComparison.OrdinalIgnoreCase))
            {
                _thwargFilterLoaded = true;
                return true;
            }
        }

        return false;
    }
}
