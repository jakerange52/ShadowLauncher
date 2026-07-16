using ShadowFilter.Paths;

namespace ShadowFilter.Channels;

internal sealed class ChannelWriter
{
    private const string LogCategory = nameof(ChannelWriter);

    public void WriteCommandsToFile(Channel channel)
    {
        channel.NeedsToWrite = false;
        var filepath = GetChannelOutboundFilepath(channel);
        try
        {
            var writer = new CommandWriter();
            var cmdset = new CommandSet(channel.GetOutboundCommands(), channel.LastInboundProcessedUtc);
            writer.WriteCommandsToFile(cmdset, filepath);
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, $"Failed to write outbound commands: {filepath}", ex);
            channel.NeedsToWrite = true;
        }
    }

    public void ReadCommandsFromFile(Channel channel)
    {
        var filepath = GetChannelInboundFilepath(channel);
        try
        {
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
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, $"Failed to read inbound commands: {filepath}", ex);
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
        try
        {
            channel.FileWatcher.Path = Path.GetDirectoryName(cmdFilepath)!;
            channel.FileWatcher.Filter = Path.GetFileName(cmdFilepath);
            channel.FileWatcher.NotifyFilter = NotifyFilters.LastWrite;
            channel.FileWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, $"Failed to start channel watcher: {cmdFilepath}", ex);
        }
    }

    public void StopWatcher(Channel channel)
    {
        channel.FileWatcher.EnableRaisingEvents = false;
    }

    public bool IsWatcherEnabled(Channel channel) => channel.FileWatcher.EnableRaisingEvents;
}
