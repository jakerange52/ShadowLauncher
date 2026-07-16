namespace ShadowLauncher.Infrastructure.Channels;

internal sealed class GameCommand
{
    public DateTime TimeStampUtc { get; }
    public string CommandString { get; }

    public GameCommand(DateTime timeStampUtc, string commandString)
    {
        TimeStampUtc = timeStampUtc;
        CommandString = commandString ?? string.Empty;
    }
}

internal sealed class GameCommandSet
{
    public IList<GameCommand> Commands { get; }
    public DateTime Acknowledgement { get; }

    public GameCommandSet(IList<GameCommand> commands, DateTime acknowledgement)
    {
        Commands = commands ?? new List<GameCommand>();
        Acknowledgement = acknowledgement;
    }
}
