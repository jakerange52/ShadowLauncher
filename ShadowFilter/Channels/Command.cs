namespace ShadowFilter.Channels;

internal sealed class Command
{
    public DateTime TimeStampUtc { get; }
    public string CommandString { get; }

    public Command(DateTime timeStampUtc, string commandString)
    {
        TimeStampUtc = timeStampUtc;
        CommandString = commandString ?? string.Empty;
    }
}
