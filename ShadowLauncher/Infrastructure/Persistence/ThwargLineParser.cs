namespace ShadowLauncher.Infrastructure.Persistence;
internal static class ThwargLineParser
{
    internal static Dictionary<string, string> Parse(string line)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in SplitEncoded(line, ','))
        {
            var eqIndex = FindUnescapedEquals(pair);
            if (eqIndex <= 0) continue;
            result[Decode(pair[..eqIndex])] = Decode(pair[(eqIndex + 1)..]);
        }
        return result;
    }
    private static List<string> SplitEncoded(string text, char delimiter)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '^' && i + 1 < text.Length) { sb.Append(text[i]); sb.Append(text[++i]); }
            else if (text[i] == delimiter) { parts.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(text[i]);
        }
        parts.Add(sb.ToString()); return parts;
    }
    private static int FindUnescapedEquals(string text)
    {
        for (int i = 0; i < text.Length; i++)
        { if (text[i] == '^' && i + 1 < text.Length) i++; else if (text[i] == '=') return i; }
        return -1;
    }
    private static string Decode(string text) => text.Replace("^e", "=").Replace("^c", ",").Replace("^u", "^");
    internal static string Encode(string text) => text.Replace("^", "^u").Replace(",", "^c").Replace("=", "^e");
}
