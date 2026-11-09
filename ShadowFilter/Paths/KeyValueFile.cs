using System.Globalization;

namespace ShadowFilter.Paths;

internal static class KeyValueFile
{
    public static Dictionary<string, string> Parse(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            // Prefer '=' when it appears before ':' so Thwarg-style
            // Timestamp=TimeUtc:'...' is not split on the colon inside TimeUtc.
            var eq = trimmed.IndexOf('=');
            var colon = trimmed.IndexOf(':');
            int sep;
            if (eq > 0 && (colon < 0 || eq < colon))
                sep = eq;
            else if (colon > 0)
                sep = colon;
            else
                continue;

            map[trimmed.Substring(0, sep).Trim()] = trimmed.Substring(sep + 1).Trim();
        }

        return map;
    }

    public static string GetString(Dictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) ? value : string.Empty;

    public static DateTime ParseTimestamp(Dictionary<string, string> settings, string key = "Timestamp")
    {
        if (!settings.TryGetValue(key, out var raw))
            return DateTime.MinValue;

        return ParseTimestampValue(raw);
    }

    public static DateTime ParseTimestampValue(string raw)
    {
        var value = raw.Trim();
        if (value.StartsWith("TimeUtc:", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("TimeUtc:".Length).Trim('\'', '"');

        return DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.MinValue;
    }
}
