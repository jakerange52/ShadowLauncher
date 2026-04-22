using System.Net.Http;
using System.Xml.Linq;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.WebServices;

/// <summary>
/// Downloads and parses the community DAT registry XML, which lists all known
/// DAT sets (retail, Dark Majesty, etc.) along with per-file download URLs
/// and optional SHA-256 checksums.
///
/// Registry URL is stored in AppConfiguration and can point to any HTTP source
/// (GitHub raw, a community-hosted file, etc.).
///
/// Expected XML schema:
/// <code>
/// &lt;DatRegistry&gt;
///   &lt;DatSet id="dark-majesty" name="Dark Majesty" version="1.0"&gt;
///     &lt;Description&gt;...&lt;/Description&gt;
///     &lt;File name="client_portal.dat"
///           url="https://example.com/dm/client_portal.dat"
///           sha256="ABCD..."
///           size="1234567890"/&gt;
///     ...
///   &lt;/DatSet&gt;
/// &lt;/DatRegistry&gt;
/// </code>
/// </summary>
public class DatRegistryDownloader
{
    // Default registry URL — can be overridden via AppConfiguration.DatRegistryUrl.
    private const string DefaultRegistryUrl =
        "https://raw.githubusercontent.com/jakerange52/ac-dat-registry/main/DatRegistry.xml";

    private const string CacheFileName = "DatRegistry.xml";

    private readonly string _cachePath;
    private readonly string _registryUrl;

    public DatRegistryDownloader(IConfigurationProvider config)
    {
        _cachePath = Path.Combine(config.DataDirectory, CacheFileName);

        // Allow the registry URL to be overridden in settings; fall back to the default.
        var configured = config.GetSetting("DatRegistryUrl");
        _registryUrl = string.IsNullOrWhiteSpace(configured) ? DefaultRegistryUrl : configured;
    }

    /// <summary>
    /// Downloads the DAT registry from the configured URL and parses it into
    /// a list of <see cref="DatSet"/> objects. Falls back to the cached copy
    /// if the network is unavailable.
    /// </summary>
    public async Task<IReadOnlyList<DatSet>> FetchDatSetsAsync()
    {
        string xml;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            xml = await http.GetStringAsync(_registryUrl);

            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            await File.WriteAllTextAsync(_cachePath, xml);
        }
        catch
        {
            // Try the AppData cache written by a previous successful download.
            if (File.Exists(_cachePath))
                xml = await File.ReadAllTextAsync(_cachePath);
            else
                return [];
        }

        return ParseXml(xml);
    }

    private static List<DatSet> ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        var sets = new List<DatSet>();

        foreach (var setEl in doc.Descendants("DatSet"))
        {
            var id = setEl.Attribute("id")?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(id)) continue;

            var set = new DatSet
            {
                Id = id,
                Name = setEl.Attribute("name")?.Value?.Trim() ?? id,
                Version = setEl.Attribute("version")?.Value?.Trim() ?? string.Empty,
                Description = setEl.Element("Description")?.Value?.Trim() ?? string.Empty,
            };

            var zipEl = setEl.Element("Zip");
            if (zipEl is not null)
            {
                set.ZipUrl = zipEl.Attribute("url")?.Value?.Trim() ?? string.Empty;
                set.ZipSha256 = zipEl.Attribute("sha256")?.Value?.Trim() ?? string.Empty;
            }

            foreach (var fileEl in setEl.Elements("File"))
            {
                var fileName = fileEl.Attribute("name")?.Value?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(fileName)) continue;

                set.Files.Add(new DatFile
                {
                    FileName = fileName,
                    DownloadUrl = fileEl.Attribute("url")?.Value?.Trim() ?? string.Empty,
                    Sha256 = fileEl.Attribute("sha256")?.Value?.Trim() ?? string.Empty,
                });
            }

            foreach (var serverEl in setEl.Descendants("Server"))
            {
                var sName = serverEl.Attribute("name")?.Value?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(sName))
                    set.ServerNames.Add(sName);
            }

            sets.Add(set);
        }

        return sets;
    }
}
