using System.Xml.Linq;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.WebServices;

internal static class ServerListFetcher
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    internal static async Task<string?> FetchXmlWithCacheAsync(string url, string cachePath)
    {
        try
        {
            var xml = await _http.GetStringAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, xml);
            return xml;
        }
        catch
        {
            return File.Exists(cachePath) ? await File.ReadAllTextAsync(cachePath) : null;
        }
    }

    internal static Server? ParseCommonFields(XElement item)
    {
        var name = item.Element("name")?.Value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) return null;

        var emuText = item.Element("emu")?.Value?.Trim() ?? "ACE";
        var emulator = emuText.Equals("GDL", StringComparison.OrdinalIgnoreCase)
                    || emuText.Equals("GDLE", StringComparison.OrdinalIgnoreCase)
            ? EmulatorType.GDLE : EmulatorType.ACE;

        _ = int.TryParse(item.Element("server_port")?.Value?.Trim(), out var port);
        if (port <= 0) port = 9000;

        return new Server
        {
            Id              = (item.Element("id")?.Value?.Trim() ?? name).ToLowerInvariant(),
            Name            = name,
            Description     = item.Element("description")?.Value?.Trim() ?? string.Empty,
            Emulator        = emulator,
            Hostname        = item.Element("server_host")?.Value?.Trim() ?? string.Empty,
            Port            = port,
            DiscordUrl      = item.Element("discord_url")?.Value?.Trim() ?? string.Empty,
            WebsiteUrl      = item.Element("website_url")?.Value?.Trim() ?? string.Empty,
            PublishedStatus = item.Element("status")?.Value?.Trim() ?? string.Empty,
        };
    }
}
