using System.Xml.Linq;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.WebServices;

/// <summary>
/// Downloads the beta server list XML from the DAT registry repo, caches it locally,
/// and parses it into Server objects with <see cref="Server.IsBeta"/> = true.
/// </summary>
public class BetaServerListDownloader
{
    private const string BetaServerListUrl =
        "https://raw.githubusercontent.com/jakerange52/ac-dat-registry/main/BetaServerList.xml";

    private const string CacheFileName = "BetaServerList.xml";

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _cachePath;

    public BetaServerListDownloader(IConfigurationProvider config)
    {
        _cachePath = Path.Combine(config.DataDirectory, CacheFileName);
    }

    /// <summary>
    /// Downloads the beta server list and caches it locally.
    /// Falls back to the cached copy on network failure.
    /// Returns an empty list if neither is available.
    /// </summary>
    public async Task<IReadOnlyList<Server>> FetchServersAsync()
    {
        string xml;
        try
        {
            xml = await _http.GetStringAsync(BetaServerListUrl);

            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllTextAsync(_cachePath, xml);
        }
        catch
        {
            if (File.Exists(_cachePath))
                xml = await File.ReadAllTextAsync(_cachePath);
            else
                return [];
        }

        return ParseXml(xml);
    }

    private static List<Server> ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var servers = new List<Server>();

        foreach (var item in doc.Descendants("ServerItem"))
        {
            var name = item.Element("name")?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var emuText = item.Element("emu")?.Value?.Trim() ?? "ACE";
            var emulator = emuText.Equals("GDL", StringComparison.OrdinalIgnoreCase)
                        || emuText.Equals("GDLE", StringComparison.OrdinalIgnoreCase)
                ? EmulatorType.GDLE
                : EmulatorType.ACE;

            _ = int.TryParse(item.Element("server_port")?.Value?.Trim(), out var port);
            if (port <= 0) port = 9000;

            var datZipUrl = item.Element("dat_zip_url")?.Value?.Trim() ?? string.Empty;

            servers.Add(new Server
            {
                Id            = (item.Element("id")?.Value?.Trim() ?? name).ToLowerInvariant(),
                Name          = name,
                Description   = item.Element("description")?.Value?.Trim() ?? string.Empty,
                Emulator      = emulator,
                Hostname      = item.Element("server_host")?.Value?.Trim() ?? string.Empty,
                Port          = port,
                DiscordUrl    = item.Element("discord_url")?.Value?.Trim() ?? string.Empty,
                WebsiteUrl    = item.Element("website_url")?.Value?.Trim() ?? string.Empty,
                PublishedStatus = item.Element("status")?.Value?.Trim() ?? string.Empty,
                DefaultRodat  = item.Element("default_rodat")?.Value
                                    ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
                SecureLogon   = item.Element("default_secure")?.Value
                                    ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
                CustomDatZipUrl = string.IsNullOrWhiteSpace(datZipUrl) ? null : datZipUrl,
                IsManuallyAdded = false,
                IsBeta          = true,
            });
        }

        return servers;
    }
}
