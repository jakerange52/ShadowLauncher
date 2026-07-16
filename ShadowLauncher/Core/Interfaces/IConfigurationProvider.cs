namespace ShadowLauncher.Core.Interfaces;

// TODO: Re-evaluate this interface. Only one implementation exists (AppConfiguration), and
// every new setting forces an interface change. Load()/Save() also leak persistence details
// to consumers. Consider deleting the interface and depending on AppConfiguration directly,
// or splitting into focused role-based interfaces (e.g. IPathProvider, IGameOptions).
public interface IConfigurationProvider
{
    string DataDirectory { get; }
    string LogDirectory { get; }

    /// <summary>Root folder where per-set DAT caches are stored.</summary>
    string DatSetsDirectory { get; }

    string GameClientPath { get; set; }
    string DecalPath { get; set; }
    string Theme { get; set; }
    bool KillOnMissingHeartbeat { get; set; }
    int KillHeartbeatTimeoutSeconds { get; set; }
    bool AutoRelaunch { get; set; }
    int AutoRelaunchDelaySeconds { get; set; }
    int MultiLaunchDelaySeconds { get; set; }
    bool NeverKillClients { get; set; }
    bool DatDeveloperMode { get; set; }
    bool AttemptDecalInjection { get; set; }
    bool SaveGameWindows { get; set; }
    bool RestoreGameWindows { get; set; }
    void Load();
    void Save();
    string GetSetting(string key, string defaultValue = "");
    void SetSetting(string key, string value);
}
