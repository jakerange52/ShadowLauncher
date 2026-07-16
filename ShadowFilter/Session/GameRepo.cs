namespace ShadowFilter.Session;

internal sealed class GameCharacter
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GameRepoState
{
    public string Server { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Character { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;

    public void SetServer(string server) => Server = server ?? string.Empty;
    public void SetAccount(string account) => Account = account ?? string.Empty;
    public void SetCharacter(string character) => Character = character ?? string.Empty;
    public void SetZoneName(string zoneName) => ZoneName = zoneName ?? string.Empty;
}

internal static class GameRepo
{
    public static readonly GameRepoState Game = new();
}
