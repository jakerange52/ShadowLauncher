using System.Text.Json;
using ShadowLauncher.Core.Interfaces;

namespace ShadowLauncher.Infrastructure.Configuration;

public class AppConfiguration : IConfigurationProvider
{
    private readonly string _settingsFilePath;
    private Dictionary<string, string> _settings = [];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppConfiguration()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShadowLauncher");
        LogDirectory = Path.Combine(DataDirectory, "Logs");
        DatSetsDirectory = Path.Combine(DataDirectory, "DatSets");
        _settingsFilePath = Path.Combine(DataDirectory, "settings.json");
    }

    public string DataDirectory { get; }
    public string LogDirectory { get; }

    /// <inheritdoc/>
    public string DatSetsDirectory { get; }

    public string GameClientPath
    {
        get => GetSetting(nameof(GameClientPath));
        set => SetSetting(nameof(GameClientPath), value);
    }

    public string DecalPath
    {
        get => GetSetting(nameof(DecalPath));
        set => SetSetting(nameof(DecalPath), value);
    }

    public string Theme
    {
        get => GetSetting(nameof(Theme), "Shadow");
        set => SetSetting(nameof(Theme), value);
    }

    public bool AutoRelaunch
    {
        get => GetBool(nameof(AutoRelaunch));
        set => SetSetting(nameof(AutoRelaunch), value.ToString());
    }

    public int AutoRelaunchDelaySeconds
    {
        get => GetInt(nameof(AutoRelaunchDelaySeconds), 10, min: 1);
        set => SetSetting(nameof(AutoRelaunchDelaySeconds), Math.Max(1, value).ToString());
    }

    public int MultiLaunchDelaySeconds
    {
        get => GetInt(nameof(MultiLaunchDelaySeconds), 1, min: 0);
        set => SetSetting(nameof(MultiLaunchDelaySeconds), Math.Max(0, value).ToString());
    }

    public bool KillOnMissingHeartbeat
    {
        get => GetBool(nameof(KillOnMissingHeartbeat));
        set => SetSetting(nameof(KillOnMissingHeartbeat), value.ToString());
    }

    public int KillHeartbeatTimeoutSeconds
    {
        get => GetInt(nameof(KillHeartbeatTimeoutSeconds), 60, min: 5);
        set => SetSetting(nameof(KillHeartbeatTimeoutSeconds), Math.Max(5, value).ToString());
    }

    public bool DatDeveloperMode
    {
        get => GetBool(nameof(DatDeveloperMode));
        set => SetSetting(nameof(DatDeveloperMode), value.ToString());
    }

    public void Load()
    {
        if (!File.Exists(_settingsFilePath)) return;
        try
        {
            var json = File.ReadAllText(_settingsFilePath);
            _settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            // Corrupt or unreadable settings file — start fresh rather than block startup.
            _settings = [];
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        // Atomic write: stage to a temp file then move into place so a crash mid-write
        // can't leave settings.json corrupted.
        var tempPath = _settingsFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }

    public string GetSetting(string key, string defaultValue = "")
        => _settings.TryGetValue(key, out var value) ? value : defaultValue;

    public void SetSetting(string key, string value)
        => _settings[key] = value;

    private bool GetBool(string key, bool defaultValue = false)
        => bool.TryParse(GetSetting(key, defaultValue.ToString()), out var v) ? v : defaultValue;

    private int GetInt(string key, int defaultValue, int min = int.MinValue)
        => int.TryParse(GetSetting(key, defaultValue.ToString()), out var v) && v >= min ? v : defaultValue;
}
