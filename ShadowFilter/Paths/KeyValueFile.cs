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

            var sep = trimmed.IndexOf(':');
            if (sep <= 0)
            {
                sep = trimmed.IndexOf('=');
                if (sep <= 0)
                    continue;
            }

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
