namespace ShadowLauncher.Infrastructure.Paths;

/// <summary>
/// Runtime data paths under %LocalAppData%\ShadowLauncher\ shared with ShadowFilter.
/// Also exposes ThwargFilter launch-file paths so both filters can run in parallel.
/// </summary>
public static class ShadowLauncherPaths
{
    public static string AppFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShadowLauncher");

    public static string LaunchFilesFolder => Path.Combine(AppFolder, "LaunchFiles");
    public static string RunningFolder => Path.Combine(AppFolder, "Running");
    public static string LoginCommandsFolder => Path.Combine(AppFolder, "LoginCommands");
    public static string CharactersFolder => Path.Combine(AppFolder, "characters");
    public static string DefaultCharactersFile => Path.Combine(AppFolder, "DefaultCharacters.json");

    /// <summary>
    /// ThwargFilter IPC root (%AppData%\ThwargLauncher). ShadowLauncher dual-writes
    /// launch files here so users who already have ThwargFilter do not need ShadowFilter.
    /// </summary>
    public static string ThwargLauncherAppFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ThwargLauncher");

    public static string ThwargLaunchFilesFolder =>
        Path.Combine(ThwargLauncherAppFolder, "LaunchFiles");

    public static string ThwargRunningFolder =>
        Path.Combine(ThwargLauncherAppFolder, "Running");

    public static string ThwargLoginCommandsFolder =>
        Path.Combine(ThwargLauncherAppFolder, "LoginCommands");

    public static string ThwargCharactersFolder =>
        Path.Combine(ThwargLauncherAppFolder, "characters");

    public static string GetShadowFilterLaunchFilePath(string serverName, string accountName) =>
        Path.Combine(LaunchFilesFolder, $"launch_ShadowFilter_{serverName}_{accountName}.txt");

    public static string GetThwargFilterLaunchFilePath(string serverName, string accountName) =>
        Path.Combine(ThwargLaunchFilesFolder, $"launch_ThwargFilter_{serverName}_{accountName}.txt");

    public static string GetHeartbeatFilePath(int processId) =>
        Path.Combine(RunningFolder, $"game_{processId}.txt");

    public static string GetThwargFilterHeartbeatFilePath(int processId) =>
        Path.Combine(ThwargRunningFolder, $"game_{processId}.txt");
}
