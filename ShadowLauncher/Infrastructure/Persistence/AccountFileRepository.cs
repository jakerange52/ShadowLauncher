using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Infrastructure.Persistence;

/// <summary>
/// Reads and writes accounts in ThwargLauncher's Accounts.txt format.
/// Format: Version=2, then one line per account: Name=xxx,Password=xxx[,Alias=xxx,LaunchPath=xxx,PreferencePath=xxx]
/// Encoding: ^c = comma, ^e = equals, ^u = caret.
/// </summary>
public sealed class AccountFileRepository : IRepository<Account>, IDisposable
{
    private readonly string _filePath;
    private readonly FileSystemWatcher _watcher;
    private List<Account> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const string HeaderComment = "# Name=xxx,Password=xxx,LaunchPath=c:\\xxx,PreferencePath=c:\\xxx,Alias=xxx";

    /// <summary>Raised whenever the backing file is modified (externally or internally).</summary>
    public event EventHandler? AccountsChanged;

    public AccountFileRepository(string filePath)
    {
        _filePath = filePath;

        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        if (!File.Exists(filePath))
            File.WriteAllText(filePath, HeaderComment + Environment.NewLine + "Version=2" + Environment.NewLine);

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
        AccountsChanged?.Invoke(this, EventArgs.Empty);
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

            var lines = File.ReadAllLines(_filePath);
            var accounts = new List<Account>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('\''))
                    continue;
                if (trimmed.StartsWith("Version="))
                    continue;

                var properties = ThwargLineParser.Parse(trimmed);
                if (!properties.TryGetValue("Name", out var name) || string.IsNullOrEmpty(name))
                    continue;
                if (!properties.TryGetValue("Password", out var password))
                    password = string.Empty;

                var account = new Account
                {
                    Id = name.ToLowerInvariant(),
                    Name = name,
                    PasswordHash = password,
                    IsActive = true,
                    CreatedDate = DateTime.UtcNow
                };

                // Store optional Thwarg properties in Notes for round-tripping
                if (properties.TryGetValue("Alias", out var alias) && !string.IsNullOrEmpty(alias))
                    account.Notes = alias;

                accounts.Add(account);
            }

            _cache = accounts;
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
            var lines = new List<string> { HeaderComment, "Version=2" };
            foreach (var a in _cache)
            {
                var parts = new List<string>
                {
                    $"Name={ThwargLineParser.Encode(a.Name)}",
                    $"Password={ThwargLineParser.Encode(a.PasswordHash)}"
                };
                if (!string.IsNullOrEmpty(a.Notes))
                    parts.Add($"Alias={ThwargLineParser.Encode(a.Notes)}");

                lines.Add(string.Join(",", parts));
            }
            File.WriteAllLines(_filePath, lines);
        }
        finally
        {
            _watcher.EnableRaisingEvents = true;
        }
    }

    public Task<Account?> GetByIdAsync(string id)
    {
        var account = _cache.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(account);
    }

    public Task<IEnumerable<Account>> GetAllAsync()
        => Task.FromResult<IEnumerable<Account>>(_cache.ToList());

    public Task<IEnumerable<Account>> FindAsync(Func<Account, bool> predicate)
        => Task.FromResult<IEnumerable<Account>>(_cache.Where(predicate).ToList());

    public Task<Account> AddAsync(Account entity)
    {
        _lock.Wait();
        try
        {
            if (_cache.Any(a => a.Name.Equals(entity.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Account '{entity.Name}' already exists.");

            entity.Id = entity.Name.ToLowerInvariant();
            _cache.Add(entity);
            SaveToFile();
            return Task.FromResult(entity);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task UpdateAsync(Account entity)
    {
        _lock.Wait();
        try
        {
            var index = _cache.FindIndex(a => a.Id.Equals(entity.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _cache[index] = entity;
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
            _cache.RemoveAll(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            SaveToFile();
            return Task.CompletedTask;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<int> CountAsync() => Task.FromResult(_cache.Count);

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
