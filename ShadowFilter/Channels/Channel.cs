namespace ShadowFilter.Channels;

internal sealed class Channel
{
    private static readonly object Locker = new();

    public static Channel MakeGameChannel()
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        return new Channel(true, pid);
    }

    private Channel(bool inGameDll, int processId)
    {
        InGameDll = inGameDll;
        ProcessId = processId;
    }

    public bool InGameDll { get; }
    public int ProcessId { get; }

    private readonly List<Command> _outboundCommands = new();
    private readonly List<Command> _inboundCommands = new();

    public DateTime LastInboundProcessedUtc { get; set; } = DateTime.MinValue;
    public bool NeedsToWrite { get; set; }
    public FileSystemWatcher FileWatcher { get; } = new();

    public IList<Command> GetOutboundCommands() => _outboundCommands;

    public bool HasInboundCommandCount() => _inboundCommands.Count > 0;

    public void EnqueueOutbound(Command cmd)
    {
        lock (Locker)
        {
            _outboundCommands.Add(cmd);
            NeedsToWrite = true;
        }
    }

    public void EnqueueInbound(Command cmd)
    {
        lock (Locker)
        {
            _inboundCommands.Add(cmd);
        }
    }

    public Command? DequeueInbound()
    {
        lock (Locker)
        {
            if (_inboundCommands.Count == 0)
                return null;

            var cmd = _inboundCommands[0];
            _inboundCommands.RemoveAt(0);
            return cmd;
        }
    }

    public void ProcessAcknowledgement(DateTime ackTimeUtc)
    {
        var pending = new List<Command>(_outboundCommands.Count);
        var changed = false;
        foreach (var cmd in _outboundCommands)
        {
            if (cmd.TimeStampUtc > ackTimeUtc)
                pending.Add(cmd);
            else
                changed = true;
        }

        _outboundCommands.Clear();
        _outboundCommands.AddRange(pending);
        if (changed)
            NeedsToWrite = true;
    }
}
