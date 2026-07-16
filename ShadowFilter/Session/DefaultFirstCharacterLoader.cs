using System.Collections.ObjectModel;
using System.Xml;

namespace ShadowFilter.Session;

internal static class DefaultFirstCharacterLoader
{
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "Decal Plugins",
            ShadowFilterPluginIds.FilterName,
            "ShadowFilter.xml");

    public static Collection<DefaultFirstCharacter> DefaultFirstCharacters
    {
        get
        {
            var characters = new Collection<DefaultFirstCharacter>();
            if (!File.Exists(SettingsPath))
                return characters;

            try
            {
                var doc = new XmlDocument();
                doc.Load(SettingsPath);
                var nodes = doc.SelectNodes("//CharacterSelectionScreen/DefaultLoginChars/DefaultLoginChar");
                if (nodes == null)
                    return characters;

                foreach (XmlNode node in nodes)
                {
                    if (node.Attributes == null)
                        continue;

                    characters.Add(new DefaultFirstCharacter(
                        node.Attributes["Server"]?.Value ?? string.Empty,
                        node.Attributes["ZoneId"]?.Value ?? string.Empty,
                        node.Attributes["CharacterName"]?.Value ?? string.Empty));
                }
            }
            catch { }

            return characters;
        }
    }

    public static void SetDefaultFirstCharacter(DefaultFirstCharacter entry)
    {
        var characters = new List<DefaultFirstCharacter>(DefaultFirstCharacters);
        characters.RemoveAll(c =>
            c.Server == entry.Server && c.ZoneId == entry.ZoneId);
        characters.Add(entry);
        Save(characters);
    }

    public static void DeleteDefaultFirstCharacter(string server, string zoneId)
    {
        var characters = new List<DefaultFirstCharacter>(DefaultFirstCharacters);
        characters.RemoveAll(c => c.Server == server && c.ZoneId == zoneId);
        Save(characters);
    }

    private static void Save(IReadOnlyList<DefaultFirstCharacter> characters)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

        var doc = new XmlDocument();
        var decl = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
        doc.AppendChild(decl);

        var root = doc.CreateElement("CharacterSelectionScreen");
        doc.AppendChild(root);

        var container = doc.CreateElement("DefaultLoginChars");
        root.AppendChild(container);

        foreach (var character in characters)
        {
            var node = doc.CreateElement("DefaultLoginChar");
            node.SetAttribute("Server", character.Server);
            node.SetAttribute("ZoneId", character.ZoneId);
            node.SetAttribute("CharacterName", character.CharacterName);
            container.AppendChild(node);
        }

        doc.Save(SettingsPath);
    }
}
