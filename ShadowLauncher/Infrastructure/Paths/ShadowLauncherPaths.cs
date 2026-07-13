namespace ShadowLauncher.Infrastructure.Paths;

/// <summary>
/// Runtime data paths under %LocalAppData%\ShadowLauncher\ shared with ShadowFilter.
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

    public static string GetShadowFilterLaunchFilePath(string serverName, string accountName) =>
        Path.Combine(LaunchFilesFolder, $"launch_ShadowFilter_{serverName}_{accountName}.txt");

    public static string GetHeartbeatFilePath(int processId) =>
        Path.Combine(RunningFolder, $"game_{processId}.txt");
}
