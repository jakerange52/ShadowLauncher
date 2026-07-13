namespace ShadowFilter.Paths;

internal static class FilterPaths
{
    public static string AppFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShadowLauncher");

    public static string LaunchFilesFolder => Ensure(Path.Combine(AppFolder, "LaunchFiles"));
    public static string RunningFolder => Ensure(Path.Combine(AppFolder, "Running"));
    public static string LoginCommandsFolder => Ensure(Path.Combine(AppFolder, "LoginCommands"));
    public static string CharactersFolder => Ensure(Path.Combine(AppFolder, "characters"));

    public static string GetLaunchFilePath(string serverName, string accountName)
    {
        var filename = $"launch_{ShadowFilterPluginIds.FilterName}_{serverName}_{accountName}.txt";
        return Path.Combine(LaunchFilesFolder, filename);
    }

    public static string GetHeartbeatFilePath(int processId)
    {
        return Path.Combine(RunningFolder, $"game_{processId}.txt");
    }

    public static string GetCharacterFilePath(string serverName, string accountName)
    {
        var filename = $"characters_{serverName}_{accountName}.txt";
        return Path.Combine(CharactersFolder, filename);
    }

    public static void EnsureDataFoldersExist()
    {
        _ = LaunchFilesFolder;
        _ = RunningFolder;
        _ = LoginCommandsFolder;
        _ = CharactersFolder;
    }

    private static string Ensure(string folder)
    {
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
        return folder;
    }
}
