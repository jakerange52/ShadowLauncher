namespace ShadowLauncher.Core.Models;

public class LaunchProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> SelectedAccountIds { get; set; } = [];
    public List<string> SelectedServerIds { get; set; } = [];
    public bool KillOnMissingHeartbeat { get; set; }
    public int KillHeartbeatTimeoutSeconds { get; set; } = 60;
    public bool AutoRelaunch { get; set; }
    public int AutoRelaunchDelaySeconds { get; set; } = 10;

    /// <summary>Global login commands (run for every character).</summary>
    public string GlobalLoginCommands { get; set; } = string.Empty;
    public int GlobalLoginCommandsWaitMs { get; set; } = 3000;

    /// <summary>
    /// Per-character login commands.
    /// Key: "accountName|serverName|characterName"
    /// </summary>
    public Dictionary<string, ProfileCharacterCommands> CharacterLoginCommands { get; set; } = [];

    /// <summary>
    /// Default character selections per account/server.
    /// Key: "accountName|serverName", Value: character name (or "any")
    /// </summary>
    public Dictionary<string, string> DefaultCharacters { get; set; } = [];
}

public class ProfileCharacterCommands
{
    public string Commands { get; set; } = string.Empty;
    public int WaitMs { get; set; } = 3000;
}
