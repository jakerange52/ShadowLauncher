using System.Text.Json;

namespace ShadowLauncher.Services.LoginCommands;

/// <summary>
/// Manages login commands that ThwargFilter executes after a character logs in.
/// Writes files to %AppData%\ThwargLauncher\LoginCommands\ in ThwargFilter's format.
/// Also manages default character selections per account/server.
/// </summary>
public class LoginCommandsService
{
    private static readonly string ThwargFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThwargLauncher");

    private static readonly string LoginCommandsFolder = Path.Combine(ThwargFolder, "LoginCommands");
    private static readonly string CharactersFolder = Path.Combine(ThwargFolder, "characters");

    /// <summary>
    /// Gets the global login commands (run for every character on login).
    /// </summary>
    public string GetGlobalCommands()
    {
        var path = Path.Combine(LoginCommandsFolder, "LoginCommandsGlobal.txt");
        return File.Exists(path) ? ParseCommandsFromFile(path) : string.Empty;
    }

    /// <summary>
    /// Sets the global login commands.
    /// </summary>
    public void SetGlobalCommands(string commands, int waitMs = 3000)
    {
        Directory.CreateDirectory(LoginCommandsFolder);
        var path = Path.Combine(LoginCommandsFolder, "LoginCommandsGlobal.txt");
        WriteCommandFile(path, commands, waitMs);
    }

    /// <summary>
    /// Gets login commands for a specific account/server/character combination.
    /// </summary>
    public string GetCharacterCommands(string accountName, string serverName, string characterName)
    {
        var path = GetCharacterFilePath(accountName, serverName, characterName);
        return File.Exists(path) ? ParseCommandsFromFile(path) : string.Empty;
    }

    /// <summary>
    /// Sets login commands for a specific account/server/character combination.
    /// </summary>
    public void SetCharacterCommands(string accountName, string serverName, string characterName, string commands, int waitMs = 3000)
    {
        Directory.CreateDirectory(LoginCommandsFolder);
        var path = GetCharacterFilePath(accountName, serverName, characterName);
        WriteCommandFile(path, commands, waitMs);
    }

    private string GetCharacterFilePath(string accountName, string serverName, string characterName)
        => Path.Combine(LoginCommandsFolder, $"LoginCommands-{accountName}-{serverName}-{characterName}.txt");

    private static string ParseCommandsFromFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var commands = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("Command") && !line.StartsWith("CommandCount"))
            {
                // ThwargFilter format uses ':' as separator (e.g., Command0:/say hello)
                var sepIndex = line.IndexOf(':');
                if (sepIndex < 0) sepIndex = line.IndexOf('=');
                if (sepIndex < 0) continue;
                var value = line[(sepIndex + 1)..];
                if (!string.IsNullOrEmpty(value))
                    commands.Add(value);
            }
        }
        return string.Join(Environment.NewLine, commands);
    }

    private static void WriteCommandFile(string path, string commands, int waitMs)
    {
        var lines = commands.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        using var writer = new StreamWriter(path);
        writer.WriteLine($"WaitMilliseconds:{waitMs}");
        writer.WriteLine($"CommandCount:{lines.Length}");
        for (int i = 0; i < lines.Length; i++)
        {
            writer.WriteLine($"Command{i}:{lines[i]}");
        }
    }

    /// <summary>
    /// Reads character names from ThwargFilter's character files for a given server+account.
    /// Files are at %AppData%\ThwargLauncher\characters\characters_{Server}_{Account}.txt
    /// Searches case-insensitively and also tries partial matches.
    /// </summary>
    public List<string> GetKnownCharacters(string serverName, string accountName)
    {
        var characters = new List<string>();
        if (!Directory.Exists(CharactersFolder)) return characters;

        // Try exact match first, then case-insensitive search
        var expectedFileName = $"characters_{serverName}_{accountName}.txt";
        var files = Directory.GetFiles(CharactersFolder, "characters_*_*.txt");
        var matchingFile = files.FirstOrDefault(f =>
            Path.GetFileName(f).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase));

        // If no exact match, try partial (server name might be slightly different)
        if (matchingFile is null)
        {
            matchingFile = files.FirstOrDefault(f =>
            {
                var fn = Path.GetFileName(f);
                return fn.Contains(serverName, StringComparison.OrdinalIgnoreCase)
                    && fn.Contains(accountName, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (matchingFile is null) return characters;

        try
        {
            var json = File.ReadAllText(matchingFile);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.TryGetProperty("CharacterList", out var charList))
                {
                    foreach (var charEntry in charList.EnumerateArray())
                    {
                        if (charEntry.TryGetProperty("Name", out var name) && name.GetString() is string charName)
                        {
                            characters.Add(charName);
                        }
                    }
                }
            }
        }
        catch
        {
            // If the file is malformed, just return empty
        }

        return characters;
    }

    // ═══════ Default Character Per Account/Server ═══════

    private static readonly string DefaultCharactersFile = Path.Combine(ThwargFolder, "DefaultCharacters.json");

    /// <summary>
    /// Gets the default character for an account/server combo.
    /// Returns null or "any" if no specific character is set (meaning don't auto-login a character).
    /// </summary>
    public string? GetDefaultCharacter(string accountName, string serverName)
    {
        var map = LoadDefaultCharacters();
        var key = $"{accountName}|{serverName}";
        return map.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Sets the default character for an account/server combo.
    /// </summary>
    public void SetDefaultCharacter(string accountName, string serverName, string characterName)
    {
        var map = LoadDefaultCharacters();
        var key = $"{accountName}|{serverName}";
        map[key] = characterName;
        SaveDefaultCharacters(map);
    }

    /// <summary>
    /// Saves all default character selections from a list of entries.
    /// </summary>
    public void SaveAllDefaultCharacters(IEnumerable<(string AccountName, string ServerName, string CharacterName)> entries)
    {
        var map = new Dictionary<string, string>();
        foreach (var (accountName, serverName, characterName) in entries)
        {
            map[$"{accountName}|{serverName}"] = characterName;
        }
        SaveDefaultCharacters(map);
    }

    private Dictionary<string, string> LoadDefaultCharacters()
    {
        if (!File.Exists(DefaultCharactersFile))
            return new Dictionary<string, string>();

        try
        {
            var json = File.ReadAllText(DefaultCharactersFile);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void SaveDefaultCharacters(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultCharactersFile)!);
        var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(DefaultCharactersFile, json);
    }
}
