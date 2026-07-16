using System.Diagnostics;
using System.Text;
using Decal.Adapter;
using ShadowFilter.Channels;
using ShadowFilter.Commands;
using ShadowFilter.Paths;
using ShadowFilter.Session;

namespace ShadowFilter.Monitoring;

internal static class HeartbeatWriter
{
    private const string LogCategory = nameof(HeartbeatWriter);
    private const int TimerSeconds = 3;
    private const int TimerSkipSeconds = 1;
    private const int OfflineTimeoutSeconds = 30;

    private static readonly object Locker = new();
    private static System.Timers.Timer? _timer;
    private static string _heartbeatPath = string.Empty;
    private static Func<DateTime>? _lastServerDispatchUtc;
    private static FilterCommandParser? _commandParser;
    private static Channel? _channel;
    private static DateTime _lastSendAndReceive = DateTime.MinValue;

    private static string _characterName = string.Empty;
    private static string _teamList = string.Empty;
    private static bool _loginFailureActive;

    public static void BindServerDispatchTracker(Func<DateTime> tracker) => _lastServerDispatchUtc = tracker;

    public static void SetCommandParser(FilterCommandParser parser) => _commandParser = parser;

    public static void RecordCharacterName(string characterName) => _characterName = characterName ?? string.Empty;

    /// <summary>
    /// OrderedDialog (0xF659) — e.g. "can't log on to the same account twice".
    /// Keeps refreshing ServerDispatch so IsOnline would stay true without this flag.
    /// </summary>
    public static void RecordLoginFailure() => _loginFailureActive = true;

    public static void ClearLoginFailure() => _loginFailureActive = false;

    public static void SendCommand(string commandString)
    {
        EnsureChannel();
        _channel!.EnqueueOutbound(new Command(DateTime.UtcNow, commandString));
    }

    public static void EnsureStarted() => StartIfNeeded();

    public static void Launch() => StartIfNeeded();

    public static void SendAndReceiveImmediately()
    {
        if (!Monitor.TryEnter(Locker, 1000))
            return;

        try
        {
            _timer?.Stop();
            SendAndReceiveCommands();
        }
        finally
        {
            _timer?.Start();
            Monitor.Exit(Locker);
        }
    }

    public static void StopAndDelete()
    {
        lock (Locker)
        {
            _loginFailureActive = false;

            if (_channel != null)
            {
                var writer = new ChannelWriter();
                if (writer.IsWatcherEnabled(_channel))
                    writer.StopWatcher(_channel);
            }

            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }

        PluginLog.Info(LogCategory, "Heartbeat stopped");

        try
        {
            if (!string.IsNullOrEmpty(_heartbeatPath) && File.Exists(_heartbeatPath))
                File.Delete(_heartbeatPath);
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, $"Failed to delete heartbeat file: {_heartbeatPath}", ex);
        }
    }

    private static void EnsureChannel()
    {
        if (_channel != null)
            return;

        _channel = Channel.MakeGameChannel();
    }

    private static void StartIfNeeded()
    {
        lock (Locker)
        {
            if (_timer != null)
                return;

            EnsureChannel();

            var pid = Process.GetCurrentProcess().Id;
            _heartbeatPath = FilterPaths.GetHeartbeatFilePath(pid);

            _timer = new System.Timers.Timer(TimerSeconds * 1000);
            _timer.Elapsed += (_, _) => OnTimerElapsed();
            _timer.AutoReset = true;
            _timer.Start();

            StartChannelFileWatcher();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StopAndDelete();

            WriteHeartbeat();
            PluginLog.Info(LogCategory, $"Heartbeat started (pid={pid}, path={_heartbeatPath})");
        }
    }

    private static void StartChannelFileWatcher()
    {
        if (_channel == null)
            return;

        var writer = new ChannelWriter();
        if (writer.IsWatcherEnabled(_channel))
            return;

        writer.StartWatcher(_channel);
        _channel.FileWatcher.Changed += (_, _) => SendAndReceiveImmediately();
    }

    private static void OnTimerElapsed()
    {
        if (!Monitor.TryEnter(Locker, 500))
            return;

        try
        {
            if ((DateTime.UtcNow - _lastSendAndReceive).TotalSeconds < TimerSkipSeconds)
                return;

            SendAndReceiveCommands();
        }
        finally
        {
            Monitor.Exit(Locker);
        }
    }

    private static void SendAndReceiveCommands()
    {
        var success = true;

        try
        {
            if (_commandParser != null)
                _teamList = _commandParser.GetTeamList();

            WriteHeartbeat();
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, "WriteHeartbeat failed", ex);
            success = false;
        }

        try
        {
            if (_channel != null && _channel.NeedsToWrite)
            {
                var writer = new ChannelWriter();
                writer.WriteCommandsToFile(_channel);
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, "WriteCommandsToFile failed", ex);
            success = false;
        }

        try
        {
            ReadAndProcessInboundCommands();
        }
        catch (Exception ex)
        {
            PluginLog.Warn(LogCategory, "ReadAndProcessInboundCommands failed", ex);
            success = false;
        }

        if (success)
            _lastSendAndReceive = DateTime.UtcNow;
    }

    private static void ReadAndProcessInboundCommands()
    {
        if (_channel == null || _commandParser == null)
            return;

        var writer = new ChannelWriter();
        writer.ReadCommandsFromFile(_channel);

        while (_channel.HasInboundCommandCount())
        {
            var cmd = _channel.DequeueInbound();
            if (cmd == null)
                break;

            _commandParser.ExecuteCommandFromLauncher(cmd.CommandString);

            if (cmd.TimeStampUtc > _channel.LastInboundProcessedUtc)
            {
                _channel.LastInboundProcessedUtc = cmd.TimeStampUtc;
                _channel.NeedsToWrite = true;
            }
        }
    }

    private static void WriteHeartbeat()
    {
        var lastDispatch = _lastServerDispatchUtc?.Invoke() ?? DateTime.MinValue;
        // Login-failure dialogs still receive OrderedDialog (0xF659) traffic, which would
        // keep IsOnline true forever while AutoRetryLogin hammers OK — force offline.
        var isOnline = !_loginFailureActive
            && (lastDispatch == DateTime.MinValue
                || (DateTime.UtcNow - lastDispatch).TotalSeconds < OfflineTimeoutSeconds);
        var uptime = (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
        // Do not fall back to the launch-file character — that makes stuck login look InGame.
        var actualCharacter = CharacterFilterTools.SafeCharacterName();
        if (string.Equals(actualCharacter, "LoginNotComplete", StringComparison.OrdinalIgnoreCase))
            actualCharacter = string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("FileVersion:1.0");
        sb.AppendLine($"UptimeSeconds:{uptime}");
        sb.AppendLine($"CharacterName:{_characterName}");
        sb.AppendLine($"TeamList:{_teamList}");
        sb.AppendLine($"IsOnline:{isOnline.ToString().ToLowerInvariant()}");
        sb.AppendLine($"LoginFailure:{_loginFailureActive.ToString().ToLowerInvariant()}");
        sb.AppendLine($"ActualCharacterName:{actualCharacter}");

        Directory.CreateDirectory(Path.GetDirectoryName(_heartbeatPath)!);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        using (var stream = new FileStream(_heartbeatPath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
    }
}
