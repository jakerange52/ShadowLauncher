using System.Text.Json;
using ShadowLauncher.Infrastructure.Native;
using ShadowLauncher.Infrastructure.Paths;

namespace ShadowLauncher.Infrastructure.Persistence;

/// <summary>
/// Persists per server+account WINDOWPLACEMENT strings for game window restore.
/// </summary>
public sealed class GameWindowPlacementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private Dictionary<string, string> _placements = new(StringComparer.OrdinalIgnoreCase);

    public GameWindowPlacementStore()
    {
        _filePath = Path.Combine(ShadowLauncherPaths.AppFolder, "WindowPlacements.json");
        Load();
    }

    public string? GetPlacement(string serverName, string accountName)
    {
        var key = GameWindowPlacementHelper.GetSessionKey(serverName, accountName);
        return _placements.TryGetValue(key, out var value) ? value : null;
    }

    public void SetPlacement(string serverName, string accountName, string placementString)
    {
        var key = GameWindowPlacementHelper.GetSessionKey(serverName, accountName);
        _placements[key] = placementString;
        Save();
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            _placements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _placements = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _placements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ShadowLauncherPaths.AppFolder);
        var json = JsonSerializer.Serialize(_placements, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
