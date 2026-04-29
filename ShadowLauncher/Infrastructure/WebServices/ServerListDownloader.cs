using System.Xml.Linq;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.WebServices;

/// <summary>
/// Downloads the community server list XML, caches it locally, and parses it into Server objects.
/// </summary>
public class ServerListDownloader
{
    private const string ServerListUrl =
        "https://raw.githubusercontent.com/acresources/serverslist/master/Servers.xml";

    private const string CacheFileName = "PublishedWildWestServerList.xml";

    private readonly string _cachePath;

    public ServerListDownloader(IConfigurationProvider config)
    {
        _cachePath = Path.Combine(config.DataDirectory, CacheFileName);
    }

    /// <summary>
    /// Downloads the server list from GitHub and caches it locally.
    /// Falls back to the cached copy on network failure.
    /// </summary>
    public async Task<IReadOnlyList<Server>> FetchServersAsync()
    {
        var xml = await ServerListFetcher.FetchXmlWithCacheAsync(ServerListUrl, _cachePath);
        return xml is null ? [] : ParseXml(xml);
    }

    private static List<Server> ParseXml(string xml)
    {
        var servers = new List<Server>();
        foreach (var item in XDocument.Parse(xml).Descendants("ServerItem"))
        {
            var server = ServerListFetcher.ParseCommonFields(item);
            if (server is null) continue;

            // Community list: rodat follows emulator convention (ACE = on, GDLE = off)
            server.DefaultRodat = server.Emulator == EmulatorType.ACE;
            servers.Add(server);
        }
        return servers;
    }
}
