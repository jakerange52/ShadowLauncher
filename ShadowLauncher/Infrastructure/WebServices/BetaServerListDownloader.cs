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
        var xml = await ServerListFetcher.FetchXmlWithCacheAsync(BetaServerListUrl, _cachePath);
        return xml is null ? [] : ParseXml(xml);
    }

    private static List<Server> ParseXml(string xml)
    {
        var servers = new List<Server>();
        foreach (var item in XDocument.Parse(xml).Descendants("ServerItem"))
        {
            var server = ServerListFetcher.ParseCommonFields(item);
            if (server is null) continue;

            var datZipUrl = item.Element("dat_zip_url")?.Value?.Trim();

            server.DefaultRodat = item.Element("default_rodat")?.Value
                                      ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
            server.SecureLogon  = item.Element("default_secure")?.Value
                                      ?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
            server.CustomDatZipUrl  = string.IsNullOrWhiteSpace(datZipUrl) ? null : datZipUrl;
            server.IsManuallyAdded  = false;
            server.IsBeta           = true;

            servers.Add(server);
        }
        return servers;
    }
}
