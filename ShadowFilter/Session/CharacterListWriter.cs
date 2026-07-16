using Newtonsoft.Json;
using ShadowFilter.Paths;

namespace ShadowFilter.Session;

internal sealed class ServerCharacterListByAccount
{
    public string ZoneId { get; set; } = string.Empty;
    public List<CharacterEntry> CharacterList { get; set; } = new();
}

internal sealed class CharacterEntry
{
    public string Name { get; set; } = string.Empty;
}

internal static class CharacterListWriter
{
    public static void Write(string serverName, string accountName, string zoneName, IEnumerable<GameCharacter> characters)
    {
        var key = $"{serverName}-{accountName}";
        var payload = new Dictionary<string, ServerCharacterListByAccount>
        {
            [key] = new ServerCharacterListByAccount
            {
                ZoneId = zoneName,
                CharacterList = characters
                    .Select(c => new CharacterEntry { Name = c.Name })
                    .ToList()
            }
        };

        var path = FilterPaths.GetCharacterFilePath(serverName, accountName);
        var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }
}
