using System.Globalization;
using System.Text;
using ShadowLauncher.Infrastructure.Paths;

namespace ShadowLauncher.Infrastructure.Channels;

internal static class GameCommandFile
{
    private const string MasterFileVersionCompat = "1";

    public static void WriteInboundCommands(int processId, IList<GameCommand> commands, DateTime acknowledgementUtc)
    {
        var path = GetInboundPath(processId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var sb = new StringBuilder();
        sb.AppendLine("FileVersion:1.2");
        sb.AppendLine($"Timestamp=TimeUtc:'{DateTime.UtcNow:o}'");
        sb.AppendLine($"AcknowledgementUtc:{acknowledgementUtc:o}");
        sb.AppendLine($"CommandCount:{commands.Count}");
        for (var i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            sb.AppendLine($"Command{i + 1}=TimeStampUtc:'{cmd.TimeStampUtc:o}' CommandString:'{cmd.CommandString}'");
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static GameCommandSet? ReadOutboundCommands(int processId)
    {
        var path = GetOutboundPath(processId);
        if (!File.Exists(path))
            return null;

        try
        {
            var settings = ParseKeyValueFile(path);
            if (!settings.TryGetValue("FileVersion", out var fileVersion) ||
                !fileVersion.StartsWith(MasterFileVersionCompat, StringComparison.Ordinal))
            {
                return null;
            }

            if (!settings.TryGetValue("Timestamp", out var timestampRaw))
                return null;

            var timestamp = ParseTimestamp(timestampRaw);
            if (DateTime.UtcNow - timestamp > TimeSpan.FromHours(1))
                return null;

            if (!settings.TryGetValue("AcknowledgementUtc", out var ackRaw) ||
                !DateTime.TryParse(ackRaw, null, DateTimeStyles.RoundtripKind, out var acknowledgement))
            {
                acknowledgement = DateTime.MinValue;
            }

            if (!settings.TryGetValue("CommandCount", out var countRaw) ||
                !int.TryParse(countRaw, out var count))
            {
                count = 0;
            }

            var commands = new List<GameCommand>(count);
            for (var i = 0; i < count; i++)
            {
                var key = $"Command{i + 1}";
                if (!settings.TryGetValue(key, out var raw))
                    continue;

                commands.Add(new GameCommand(
                    ParseEmbeddedDate(raw, "TimeStampUtc"),
                    ParseEmbeddedString(raw, "CommandString")));
            }

            return new GameCommandSet(commands, acknowledgement.ToUniversalTime());
        }
        catch
        {
            return null;
        }
    }

    public static string GetOutboundPath(int processId) =>
        Path.Combine(ShadowLauncherPaths.RunningFolder, $"outcmds_{processId}.txt");

    public static string GetInboundPath(int processId) =>
        Path.Combine(ShadowLauncherPaths.RunningFolder, $"incmds_{processId}.txt");

    private static Dictionary<string, string> ParseKeyValueFile(string path)
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

    private static DateTime ParseTimestamp(string raw)
    {
        var value = raw.Trim();
        if (value.StartsWith("TimeUtc:", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("TimeUtc:".Length).Trim('\'', '"');

        return DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.MinValue;
    }

    private static DateTime ParseEmbeddedDate(string raw, string key)
    {
        var prefix = key + ":'";
        var start = raw.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return DateTime.MinValue;

        start += prefix.Length;
        var end = raw.IndexOf('\'', start);
        if (end < 0)
            return DateTime.MinValue;

        return DateTime.TryParse(raw.Substring(start, end - start), null, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.MinValue;
    }

    private static string ParseEmbeddedString(string raw, string key)
    {
        var prefix = key + ":'";
        var start = raw.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        start += prefix.Length;
        var end = raw.IndexOf('\'', start);
        if (end < 0)
            return string.Empty;

        return raw.Substring(start, end - start);
    }
}
