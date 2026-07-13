using ShadowFilter.Paths;

namespace ShadowFilter.Channels;

internal sealed class ChannelWriter
{
    public void WriteCommandsToFile(Channel channel)
    {
        channel.NeedsToWrite = false;
        var filepath = GetChannelOutboundFilepath(channel);
        var writer = new CommandWriter();
        var cmdset = new CommandSet(channel.GetOutboundCommands(), channel.LastInboundProcessedUtc);
        writer.WriteCommandsToFile(cmdset, filepath);
    }

    public void ReadCommandsFromFile(Channel channel)
    {
        var filepath = GetChannelInboundFilepath(channel);
        var writer = new CommandWriter();
        var cmdset = writer.ReadCommandsFromFile(filepath);
        if (cmdset == null)
            return;

        channel.ProcessAcknowledgement(cmdset.Acknowledgement);

        var latestUtc = channel.LastInboundProcessedUtc;
        foreach (var cmd in cmdset.Commands)
        {
            if (cmd.TimeStampUtc <= channel.LastInboundProcessedUtc)
                continue;

            if (cmd.TimeStampUtc > latestUtc)
                latestUtc = cmd.TimeStampUtc;

            channel.EnqueueInbound(cmd);
        }

        if (channel.LastInboundProcessedUtc < latestUtc)
        {
            channel.LastInboundProcessedUtc = latestUtc;
            channel.NeedsToWrite = true;
        }
    }

    public string GetChannelOutboundFilepath(Channel channel)
    {
        var prefix = channel.InGameDll ? "outcmds" : "incmds";
        return Path.Combine(FilterPaths.RunningFolder, $"{prefix}_{channel.ProcessId}.txt");
    }

    public string GetChannelInboundFilepath(Channel channel)
    {
        var prefix = channel.InGameDll ? "incmds" : "outcmds";
        return Path.Combine(FilterPaths.RunningFolder, $"{prefix}_{channel.ProcessId}.txt");
    }

    public void StartWatcher(Channel channel)
    {
        var cmdFilepath = GetChannelInboundFilepath(channel);
        channel.FileWatcher.Path = Path.GetDirectoryName(cmdFilepath)!;
        channel.FileWatcher.Filter = Path.GetFileName(cmdFilepath);
        channel.FileWatcher.NotifyFilter = NotifyFilters.LastWrite;
        channel.FileWatcher.EnableRaisingEvents = true;
    }

    public void StopWatcher(Channel channel)
    {
        channel.FileWatcher.EnableRaisingEvents = false;
    }

    public bool IsWatcherEnabled(Channel channel) => channel.FileWatcher.EnableRaisingEvents;
}
