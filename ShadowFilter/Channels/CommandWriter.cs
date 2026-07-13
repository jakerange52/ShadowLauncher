using System.Globalization;
using System.Text;
using ShadowFilter.Paths;

namespace ShadowFilter.Channels;

internal sealed class CommandWriter
{
    public const string MasterFileVersion = "1.2";
    public const string MasterFileVersionCompat = "1";

    public void WriteCommandsToFile(CommandSet cmdset, string filepath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filepath)!);
        var contents = WriteCommandsToString(cmdset);
        File.WriteAllText(filepath, contents, Encoding.UTF8);
    }

    public CommandSet? ReadCommandsFromFile(string filepath)
    {
        try
        {
            if (!File.Exists(filepath))
                return null;

            var settings = KeyValueFile.Parse(filepath);
            if (!settings.TryGetValue("FileVersion", out var fileVersion) ||
                !fileVersion.StartsWith(MasterFileVersionCompat, StringComparison.Ordinal))
            {
                return null;
            }

            if (!settings.TryGetValue("Timestamp", out var timestampRaw))
                return null;

            var timestamp = KeyValueFile.ParseTimestampValue(timestampRaw);
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

            var commands = new List<Command>(count);
            for (var i = 0; i < count; i++)
            {
                var key = $"Command{i + 1}";
                if (!settings.TryGetValue(key, out var raw))
                    continue;

                var time = ParseEmbeddedDate(raw, "TimeStampUtc");
                var cmdString = ParseEmbeddedString(raw, "CommandString");
                commands.Add(new Command(time, cmdString));
            }

            return new CommandSet(commands, acknowledgement.ToUniversalTime());
        }
        catch
        {
            return null;
        }
    }

    private static string WriteCommandsToString(CommandSet cmdset)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"FileVersion:{MasterFileVersion}");
        sb.AppendLine($"Timestamp=TimeUtc:'{DateTime.UtcNow:o}'");
        sb.AppendLine($"AcknowledgementUtc:{cmdset.Acknowledgement:o}");
        sb.AppendLine($"CommandCount:{cmdset.Commands.Count}");
        for (var i = 0; i < cmdset.Commands.Count; i++)
        {
            var cmd = cmdset.Commands[i];
            sb.AppendLine(
                $"Command{i + 1}=TimeStampUtc:'{cmd.TimeStampUtc:o}' CommandString:'{cmd.CommandString}'");
        }

        return sb.ToString();
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
