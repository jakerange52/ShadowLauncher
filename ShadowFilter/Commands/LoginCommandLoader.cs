using ShadowFilter.Paths;

namespace ShadowFilter.Commands;

internal sealed class LoginCommands
{
    public int WaitMilliseconds { get; set; } = 3000;
    public bool HasExplicitWait { get; set; }
    public Queue<string> Commands { get; } = new();
}

internal static class LoginCommandLoader
{
    private const int DefaultWaitMilliseconds = 3000;

    public static LoginCommands ReadCombined(string accountName, string serverName, string characterName)
    {
        var global = ReadFile(GetGlobalPath());
        var specific = ReadFile(GetCharacterPath(accountName, serverName, characterName));

        // Per-character wait wins when explicitly set in the character file.
        if (specific.HasExplicitWait)
            global.WaitMilliseconds = specific.WaitMilliseconds;
        else if (!global.HasExplicitWait)
            global.WaitMilliseconds = DefaultWaitMilliseconds;

        while (specific.Commands.Count > 0)
            global.Commands.Enqueue(specific.Commands.Dequeue());

        return global;
    }

    private static string GetGlobalPath() =>
        Path.Combine(FilterPaths.LoginCommandsFolder, "LoginCommandsGlobal.txt");

    private static string GetCharacterPath(string accountName, string serverName, string characterName)
    {
        var safeAccount = Sanitize(accountName);
        var safeServer = Sanitize(serverName);
        var safeCharacter = Sanitize(characterName);
        return Path.Combine(
            FilterPaths.LoginCommandsFolder,
            $"LoginCommands-{safeAccount}-{safeServer}-{safeCharacter}.txt");
    }

    private static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("/", string.Empty)
            .Replace("\\", string.Empty)
            .Replace(":", string.Empty)
            .Replace("!", string.Empty)
            .Replace("'", string.Empty)
            .Replace("?", string.Empty);
    }

    private static LoginCommands ReadFile(string path)
    {
        var commands = new LoginCommands();
        if (!File.Exists(path))
            return commands;

        var lines = File.ReadAllLines(path);
        foreach (var line in lines)
        {
            if (line.StartsWith("WaitMilliseconds:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line.Substring("WaitMilliseconds:".Length), out var waitMs))
                {
                    commands.WaitMilliseconds = waitMs;
                    commands.HasExplicitWait = true;
                }
                continue;
            }

            if (line.StartsWith("Command", StringComparison.OrdinalIgnoreCase) &&
                !line.StartsWith("CommandCount", StringComparison.OrdinalIgnoreCase))
            {
                var sep = line.IndexOf(':');
                if (sep < 0)
                    sep = line.IndexOf('=');
                if (sep < 0)
                    continue;

                var value = line.Substring(sep + 1);
                if (!string.IsNullOrWhiteSpace(value))
                    commands.Commands.Enqueue(value);
            }
        }

        return commands;
    }
}
