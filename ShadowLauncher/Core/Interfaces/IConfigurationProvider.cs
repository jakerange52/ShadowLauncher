namespace ShadowLauncher.Core.Interfaces;

public interface IConfigurationProvider
{
    string DataDirectory { get; }
    string LogDirectory { get; }

    /// <summary>Root folder where per-set DAT caches are stored.</summary>
    string DatSetsDirectory { get; }

    string GameClientPath { get; set; }
    string DecalPath { get; set; }
    TimeSpan HeartbeatInterval { get; set; }
    TimeSpan HeartbeatTimeout { get; set; }
    TimeSpan ServerCheckInterval { get; set; }
    bool NeverKillOnMissingHeartbeat { get; set; }
    bool KillOnMissingHeartbeat { get; set; }
    int KillHeartbeatTimeoutSeconds { get; set; }
    bool AutoRelaunch { get; set; }
    int AutoRelaunchDelaySeconds { get; set; }
    void Load();
    void Save();
    string GetSetting(string key, string defaultValue = "");
    void SetSetting(string key, string value);
}
