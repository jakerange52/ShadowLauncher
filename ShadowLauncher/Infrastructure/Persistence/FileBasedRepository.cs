using System.Text.Json;
using ShadowLauncher.Core.Interfaces;

namespace ShadowLauncher.Infrastructure.Persistence;

public class FileBasedRepository<T> : IRepository<T> where T : class
{
    private readonly string _filePath;
    private List<T> _cache = [];
    private readonly Func<T, string> _idSelector;
    private bool _loaded;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public FileBasedRepository(string filePath, Func<T, string> idSelector)
    {
        _filePath = filePath;
        _idSelector = idSelector;
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        await _lock.WaitAsync();
        try
        {
            if (_loaded) return;
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                _cache = JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
            }
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_cache, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }

    public async Task<T?> GetByIdAsync(string id)
    {
        await EnsureLoadedAsync();
        return _cache.FirstOrDefault(x => _idSelector(x) == id);
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return _cache.ToList();
    }

    public async Task<IEnumerable<T>> FindAsync(Func<T, bool> predicate)
    {
        await EnsureLoadedAsync();
        return _cache.Where(predicate).ToList();
    }

    public async Task<T> AddAsync(T entity)
    {
        await EnsureLoadedAsync();
        _cache.Add(entity);
        await SaveAsync();
        return entity;
    }

    public async Task UpdateAsync(T entity)
    {
        await EnsureLoadedAsync();
        var id = _idSelector(entity);
        var index = _cache.FindIndex(x => _idSelector(x) == id);
        if (index >= 0)
        {
            _cache[index] = entity;
            await SaveAsync();
        }
    }

    public async Task DeleteAsync(string id)
    {
        await EnsureLoadedAsync();
        var index = _cache.FindIndex(x => _idSelector(x) == id);
        if (index >= 0)
        {
            _cache.RemoveAt(index);
            await SaveAsync();
        }
    }

    public async Task<int> CountAsync()
    {
        await EnsureLoadedAsync();
        return _cache.Count;
    }
}
