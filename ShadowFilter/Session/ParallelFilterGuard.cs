namespace ShadowFilter.Session;

/// <summary>
/// Detects ThwargFilter in the same Decal host and decides which chat prefix ShadowFilter owns.
///
/// When only ShadowFilter is loaded, both <c>/sf</c> and <c>/tf</c> work (Thwarg muscle memory).
/// When both filters are loaded, ShadowFilter owns automation and <c>/sf</c>; <c>/tf</c> is left
/// for ThwargFilter so the two plugins do not double-eat chat commands.
/// </summary>
internal static class ParallelFilterGuard
{
    public const string ShadowPrefix = "/sf";
    public const string ThwargPrefix = "/tf";

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

    /// <summary>
    /// Prefixes ShadowFilter should honor: always <c>/sf</c>, plus <c>/tf</c> when ThwargFilter is absent.
    /// </summary>
    public static IEnumerable<string> ActiveChatPrefixes()
    {
        yield return ShadowPrefix;
        if (!IsThwargFilterLoaded())
            yield return ThwargPrefix;
    }

    /// <summary>
    /// True when <paramref name="text"/> is <c>/sf command</c> (or <c>/tf command</c> if ThwargFilter is not loaded).
    /// </summary>
    public static bool TryMatchChatCommand(string text, string command, out string remainder)
    {
        foreach (var prefix in ActiveChatPrefixes())
        {
            var full = prefix + " " + command;
            if (text.StartsWith(full, StringComparison.OrdinalIgnoreCase))
            {
                remainder = text.Length > full.Length ? text.Substring(full.Length) : string.Empty;
                return true;
            }
        }

        remainder = string.Empty;
        return false;
    }

    /// <summary>
    /// True when <paramref name="text"/> equals <c>/sf command</c> (or <c>/tf command</c> if ThwargFilter is not loaded).
    /// </summary>
    public static bool MatchesChatCommand(string text, string command)
    {
        foreach (var prefix in ActiveChatPrefixes())
        {
            if (string.Equals(text, prefix + " " + command, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
