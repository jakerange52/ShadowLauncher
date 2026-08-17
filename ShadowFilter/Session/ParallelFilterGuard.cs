namespace ShadowFilter.Session;

/// <summary>
/// Detects ThwargFilter loaded in the same Decal host. ThwargFilter-parity automation
/// (char select, login commands, login retry, lcmq, lnc, dlc, fast-quit) defers to
/// Thwarg when both are present. ShadowFilter keeps launcher IPC: heartbeat, channel
/// relay, character list files, and in-game /tf broadcast/team/kill commands.
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
