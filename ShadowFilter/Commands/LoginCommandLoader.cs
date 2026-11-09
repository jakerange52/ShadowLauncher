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
        var specificPath = ResolveCharacterFile(accountName, serverName, characterName);
        var specific = specificPath is null ? new LoginCommands() : ReadFile(specificPath);

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

    private static string? ResolveCharacterFile(string accountName, string serverName, string characterName)
    {
        if (string.IsNullOrWhiteSpace(accountName) ||
            string.IsNullOrWhiteSpace(serverName) ||
            string.IsNullOrWhiteSpace(characterName))
        {
            return null;
        }

        // Launcher writes the raw names. Older files dropped apostrophes.
        var exact = Path.Combine(
            FilterPaths.LoginCommandsFolder,
            $"LoginCommands-{accountName}-{serverName}-{characterName}.txt");
        if (File.Exists(exact))
            return exact;

        var legacy = Path.Combine(
            FilterPaths.LoginCommandsFolder,
            $"LoginCommands-{Sanitize(accountName)}-{Sanitize(serverName)}-{Sanitize(characterName)}.txt");
        if (File.Exists(legacy))
            return legacy;

        if (!Directory.Exists(FilterPaths.LoginCommandsFolder))
            return null;

        foreach (var path in Directory.GetFiles(FilterPaths.LoginCommandsFolder, "LoginCommands-*.txt"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!stem.StartsWith("LoginCommands-", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = stem.Substring("LoginCommands-".Length).Split(new[] { '-' }, 3);
            if (parts.Length != 3)
                continue;

            if (SameToken(parts[0], accountName) &&
                SameToken(parts[1], serverName) &&
                SameToken(parts[2], characterName))
            {
                return path;
            }
        }

        return null;
    }

    private static bool SameToken(string left, string right) =>
        string.Equals(Sanitize(left), Sanitize(right), StringComparison.OrdinalIgnoreCase);

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
