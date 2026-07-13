namespace ShadowFilter.Channels;

internal sealed class CommandSet
{
    public IList<Command> Commands { get; }
    public DateTime Acknowledgement { get; }

    public CommandSet(IList<Command> commands, DateTime acknowledgement)
    {
        Commands = commands ?? new List<Command>();
        Acknowledgement = acknowledgement;
    }
}
