using Decal.Adapter;

namespace ShadowFilter.Session;

internal static class CharacterFilterTools
{
    public static string SafeCharacterName(string fallback = "")
    {
        try
        {
            var name = CoreManager.Current.CharacterFilter.Name;
            return string.IsNullOrEmpty(name) ? fallback : name;
        }
        catch (Exception ex)
        {
            OomGuard.DieIfOom(ex);
            return fallback;
        }
    }
}
