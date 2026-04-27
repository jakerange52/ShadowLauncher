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

    public TimeSpan HeartbeatInterval
    {
        get => TimeSpan.FromSeconds(int.TryParse(GetSetting(nameof(HeartbeatInterval), "5"), out var v) ? v : 5);
        set => SetSetting(nameof(HeartbeatInterval), ((int)value.TotalSeconds).ToString());
    }

    public TimeSpan HeartbeatTimeout
    {
        get => TimeSpan.FromSeconds(int.TryParse(GetSetting(nameof(HeartbeatTimeout), "60"), out var v) ? v : 60);
        set => SetSetting(nameof(HeartbeatTimeout), ((int)value.TotalSeconds).ToString());
    }

    public TimeSpan ServerCheckInterval
    {
        get => TimeSpan.FromSeconds(int.TryParse(GetSetting(nameof(ServerCheckInterval), "300"), out var v) ? v : 300);
        set => SetSetting(nameof(ServerCheckInterval), ((int)value.TotalSeconds).ToString());
    }

    public bool AutoRelaunch
    {
        get => bool.TryParse(GetSetting(nameof(AutoRelaunch), "false"), out var v) && v;
        set => SetSetting(nameof(AutoRelaunch), value.ToString());
    }

    public int AutoRelaunchDelaySeconds
    {
        get => int.TryParse(GetSetting(nameof(AutoRelaunchDelaySeconds), "10"), out var v) && v > 0 ? v : 10;
        set => SetSetting(nameof(AutoRelaunchDelaySeconds), Math.Max(1, value).ToString());
    }

    public bool KillOnMissingHeartbeat
    {
        get => bool.TryParse(GetSetting(nameof(KillOnMissingHeartbeat), "false"), out var v) && v;
        set => SetSetting(nameof(KillOnMissingHeartbeat), value.ToString());
    }

    public int KillHeartbeatTimeoutSeconds
    {
        get => int.TryParse(GetSetting(nameof(KillHeartbeatTimeoutSeconds), "60"), out var v) && v > 0 ? v : 60;
        set => SetSetting(nameof(KillHeartbeatTimeoutSeconds), Math.Max(5, value).ToString());
    }

    public bool DatDeveloperMode
    {
        get => bool.TryParse(GetSetting(nameof(DatDeveloperMode), "false"), out var v) && v;
        set => SetSetting(nameof(DatDeveloperMode), value.ToString());
    }

    public void Load()
    {
        if (File.Exists(_settingsFilePath))
        {
            var json = File.ReadAllText(_settingsFilePath);
            _settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? [];
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(_settingsFilePath, json);
    }

    public string GetSetting(string key, string defaultValue = "")
        => _settings.TryGetValue(key, out var value) ? value : defaultValue;

    public void SetSetting(string key, string value)
        => _settings[key] = value;
}
