using System.Xml.Linq;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.Persistence;

/// <summary>
/// Reads and writes servers in ThwargLauncher's UserServerList.xml format.
/// XML format: &lt;ArrayOfServerItem&gt; containing &lt;ServerItem&gt; elements with
/// id, name, alias, description, emu, connect_string, discord_url, website_url,
/// default_rodat, default_secure, visibility.
/// This allows direct drag/drop of ThwargLauncher's server file.
/// </summary>
public sealed class ServerFileRepository : IRepository<Server>, IDisposable
{
    private readonly string _filePath;
    private readonly string _overridesPath;
    private readonly FileSystemWatcher _watcher;
    private List<Server> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    // Survives server remove/re-add — keyed by lowercase server name.
    private Dictionary<string, string> _datSetOverrides = [];

    public event EventHandler? ServersChanged;

    public ServerFileRepository(string filePath)
    {
        _filePath = filePath;
        _overridesPath = Path.Combine(Path.GetDirectoryName(filePath)!, "ServerDatOverrides.xml");

        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        LoadOverrides();

        if (!File.Exists(filePath))
        {
            new XDocument(new XElement("ArrayOfServerItem")).Save(filePath);
        }

        LoadFromFile();

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(filePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Thread.Sleep(100);
        LoadFromFile();
        ServersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFromFile()
    {
        _lock.Wait();
        try
        {
            if (!File.Exists(_filePath))
            {
                _cache = [];
                return;
            }

            var servers = new List<Server>();
            try
            {
                var doc = XDocument.Load(_filePath);
                var items = doc.Descendants("ServerItem");

                foreach (var item in items)
                {
                    var name = item.Element("name")?.Value ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Parse connect_string (host:port) or separate host/port elements
                    var connectString = item.Element("connect_string")?.Value;
                    string hostname;
                    int port = 9000;

                    if (!string.IsNullOrEmpty(connectString) && connectString.Contains(':'))
                    {
                        var parts = connectString.Split(':');
                        hostname = parts[0];
                        int.TryParse(parts[1], out port);
                    }
                    else
                    {
                        hostname = item.Element("server_host")?.Value ?? connectString ?? string.Empty;
                        var portStr = item.Element("server_port")?.Value;
                        if (!string.IsNullOrEmpty(portStr))
                            int.TryParse(portStr, out port);
                    }

                    var emuStr = item.Element("emu")?.Value ?? "ACE";
                    var emulator = emuStr.Equals("GDLE", StringComparison.OrdinalIgnoreCase)
                        ? EmulatorType.GDLE : EmulatorType.ACE;

                    var rodatStr = item.Element("default_rodat")?.Value ?? "Off";
                    var secureStr = item.Element("default_secure")?.Value ?? "Off";

                    var key = name.ToLowerInvariant();
                    var rawDatSetId = item.Element("dat_set_id")?.Value?.Trim() is { Length: > 0 } d ? d : null;
                    // Merge: file value wins if present, otherwise fall back to persisted override.
                    var resolvedDatSetId = rawDatSetId
                        ?? (_datSetOverrides.TryGetValue(key, out var ov) ? ov : null);

                    servers.Add(new Server
                    {
                        Id = key,
                        Name = name,
                        Description = item.Element("description")?.Value ?? string.Empty,
                        Emulator = emulator,
                        Hostname = hostname,
                        Port = port,
                        DiscordUrl = item.Element("discord_url")?.Value ?? string.Empty,
                        WebsiteUrl = item.Element("website_url")?.Value ?? string.Empty,
                        DefaultRodat = rodatStr.Equals("On", StringComparison.OrdinalIgnoreCase),
                        SecureLogon = secureStr.Equals("On", StringComparison.OrdinalIgnoreCase),
                        DatSetId = resolvedDatSetId,
                        IsManuallyAdded = item.Element("manually_added")?.Value
                            .Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
                        CustomDatRegistryPath = item.Element("custom_dat_path")?.Value is { Length: > 0 } p ? p : null,
                        CustomDatZipUrl = item.Element("custom_dat_zip_url")?.Value is { Length: > 0 } z ? z : null,
                    });
                }
            }
            catch
            {
                // If XML is malformed, start with empty list
            }

            _cache = servers;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void SaveToFile()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _watcher.EnableRaisingEvents = false;
        try
        {
            var doc = new XDocument(
                new XElement("ArrayOfServerItem",
                    _cache.Select(s => new XElement("ServerItem",
                        new XElement("id", s.Id),
                        new XElement("name", s.Name),
                        new XElement("alias", ""),
                        new XElement("description", s.Description),
                        new XElement("emu", s.Emulator.ToString().ToUpperInvariant()),
                        new XElement("connect_string", $"{s.Hostname}:{s.Port}"),
                        new XElement("discord_url", s.DiscordUrl),
                        new XElement("website_url", s.WebsiteUrl),
                        new XElement("default_rodat", s.DefaultRodat ? "On" : "Off"),
                        new XElement("default_secure", s.SecureLogon ? "On" : "Off"),
                        new XElement("dat_set_id", s.DatSetId ?? string.Empty),
                        new XElement("manually_added", s.IsManuallyAdded ? "true" : "false"),
                        new XElement("custom_dat_path", s.CustomDatRegistryPath ?? string.Empty),
                        new XElement("custom_dat_zip_url", s.CustomDatZipUrl ?? string.Empty),
                        new XElement("visibility", "Visible")
                    ))
                )
            );
            doc.Save(_filePath);
        }
        finally
        {
            _watcher.EnableRaisingEvents = true;
        }
    }

    public Task<Server?> GetByIdAsync(string id)
    {
        var server = _cache.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(server);
    }

    public Task<IEnumerable<Server>> GetAllAsync()
        => Task.FromResult<IEnumerable<Server>>(_cache.ToList());

    public Task<IEnumerable<Server>> FindAsync(Func<Server, bool> predicate)
        => Task.FromResult<IEnumerable<Server>>(_cache.Where(predicate).ToList());

    public Task<Server> AddAsync(Server entity)
    {
        _lock.Wait();
        try
        {
            var existing = _cache.FirstOrDefault(s => s.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
                throw new InvalidOperationException($"Server '{entity.Name}' already exists.");

            entity.Id = entity.Name.ToLowerInvariant();

            // Persist DatSetId override so it survives future remove/re-add.
            if (!string.IsNullOrWhiteSpace(entity.DatSetId))
            {
                _datSetOverrides[entity.Id] = entity.DatSetId;
                SaveOverrides();
            }

            _cache.Add(entity);
            SaveToFile();
            return Task.FromResult(entity);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task UpdateAsync(Server entity)
    {
        _lock.Wait();
        try
        {
            var index = _cache.FindIndex(s => s.Id.Equals(entity.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _cache[index] = entity;

                // Persist DatSetId override so it survives remove/re-add.
                var key = entity.Id;
                if (!string.IsNullOrWhiteSpace(entity.DatSetId))
                    _datSetOverrides[key] = entity.DatSetId;
                else
                    _datSetOverrides.Remove(key);
                SaveOverrides();

                SaveToFile();
            }
            return Task.CompletedTask;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task DeleteAsync(string id)
    {
        _lock.Wait();
        try
        {
            _cache.RemoveAll(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            SaveToFile();
            return Task.CompletedTask;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<int> CountAsync() => Task.FromResult(_cache.Count);

    private void LoadOverrides()
    {
        if (!File.Exists(_overridesPath)) return;
        try
        {
            var doc = XDocument.Load(_overridesPath);
            _datSetOverrides = doc.Descendants("Override")
                .Where(e => e.Attribute("server") is not null && e.Attribute("datSetId") is not null)
                .ToDictionary(
                    e => e.Attribute("server")!.Value,
                    e => e.Attribute("datSetId")!.Value);
        }
        catch { /* corrupt file — start fresh */ }
    }

    private void SaveOverrides()
    {
        var doc = new XDocument(new XElement("ServerDatOverrides",
            _datSetOverrides.Select(kv =>
                new XElement("Override",
                    new XAttribute("server", kv.Key),
                    new XAttribute("datSetId", kv.Value)))));
        doc.Save(_overridesPath);
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
