using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.FileSystem;
using ShadowLauncher.Services.GameSessions;
using ShadowLauncher.Services.LoginCommands;
using ShadowLauncher.Services.Monitoring;

namespace ShadowLauncher.Infrastructure.Channels;

/// <summary>
/// Relays ShadowFilter channel commands between clients.
/// </summary>
public sealed class ChannelRelayService
{
    private readonly IGameSessionService _sessionService;
    private readonly IHeartbeatReader _heartbeatReader;
    private readonly LoginCommandsService _loginCommandsService;
    private readonly GameWindowPlacementService _windowPlacement;
    private readonly IConfigurationProvider _config;
    private readonly ILogger<ChannelRelayService> _logger;

    private readonly Dictionary<int, DateTime> _lastProcessedOutboundUtc = new();

    public ChannelRelayService(
        IGameSessionService sessionService,
        IHeartbeatReader heartbeatReader,
        LoginCommandsService loginCommandsService,
        GameWindowPlacementService windowPlacement,
        IConfigurationProvider config,
        ILogger<ChannelRelayService> logger)
    {
        _sessionService = sessionService;
        _heartbeatReader = heartbeatReader;
        _loginCommandsService = loginCommandsService;
        _windowPlacement = windowPlacement;
        _config = config;
        _logger = logger;
    }

    public async Task ProcessActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = (await _sessionService.GetActiveSessionsAsync()).ToList();
        if (sessions.Count == 0)
            return;

        var heartbeats = new Dictionary<int, HeartbeatData?>();
        foreach (var session in sessions)
            heartbeats[session.ProcessId] = await _heartbeatReader.ReadHeartbeatAsync(session.ProcessId);

        foreach (var session in sessions)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (!IsProcessRunning(session.ProcessId))
                continue;

            var cmdset = GameCommandFile.ReadOutboundCommands(session.ProcessId);
            if (cmdset == null || cmdset.Commands.Count == 0)
                continue;

            var lastAck = _lastProcessedOutboundUtc.GetValueOrDefault(session.ProcessId);
            foreach (var cmd in cmdset.Commands)
            {
                if (cmd.TimeStampUtc <= lastAck)
                    continue;

                await HandleCommandAsync(cmd.CommandString, session, sessions, heartbeats);
                if (cmd.TimeStampUtc > lastAck)
                    lastAck = cmd.TimeStampUtc;
            }

            _lastProcessedOutboundUtc[session.ProcessId] = lastAck;
        }
    }

    private async Task HandleCommandAsync(
        string command,
        GameSession source,
        IReadOnlyList<GameSession> allSessions,
        IReadOnlyDictionary<int, HeartbeatData?> heartbeats)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        if (command.StartsWith("broadcast ", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("bc ", StringComparison.OrdinalIgnoreCase))
        {
            var payload = command.StartsWith("broadcast ", StringComparison.OrdinalIgnoreCase)
                ? command.Substring("broadcast ".Length)
                : command.Substring("bc ".Length);

            string? teamFilter = null;
            if (payload.StartsWith("/team:", StringComparison.OrdinalIgnoreCase))
            {
                var prefixLen = "/team:".Length;
                var space = payload.IndexOf(' ');
                if (space > 0)
                {
                    teamFilter = payload.Substring(prefixLen, space - prefixLen);
                    payload = payload.Substring(space + 1);
                }
            }
            else if (payload.StartsWith("/t:", StringComparison.OrdinalIgnoreCase))
            {
                var prefixLen = "/t:".Length;
                var space = payload.IndexOf(' ');
                if (space > 0)
                {
                    teamFilter = payload.Substring(prefixLen, space - prefixLen);
                    payload = payload.Substring(space + 1);
                }
            }

            foreach (var target in allSessions)
            {
                if (target.ProcessId == source.ProcessId)
                    continue;

                if (teamFilter != null)
                {
                    heartbeats.TryGetValue(target.ProcessId, out var hb);
                    var teams = hb?.TeamList ?? string.Empty;
                    if (!teams.Split(',').Any(t => string.Equals(t.Trim(), teamFilter, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                EnqueueInbound(target.ProcessId, payload);
            }

            return;
        }

        if (command.StartsWith("createteam ", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("ct ", StringComparison.OrdinalIgnoreCase))
        {
            var payload = command.StartsWith("createteam ", StringComparison.OrdinalIgnoreCase)
                ? command.Substring("createteam ".Length)
                : command.Substring("ct ".Length);

            var parts = payload.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;

            var teamName = parts[0];
            var characterNames = parts.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var target in allSessions)
            {
                if (!characterNames.Contains(target.CharacterName))
                    continue;

                EnqueueInbound(target.ProcessId, $"/tf jointeam {teamName}");
            }

            return;
        }

        if (string.Equals(command, "killclient", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "kc", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.NeverKillClients)
                return;

            await KillProcessAsync(source.ProcessId);
            return;
        }

        if (string.Equals(command, "killallclients", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "kac", StringComparison.OrdinalIgnoreCase))
        {
            if (_config.NeverKillClients)
                return;

            foreach (var target in allSessions)
                await KillProcessAsync(target.ProcessId);

            return;
        }

        if (command.StartsWith("addlogincmd ", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("alc ", StringComparison.OrdinalIgnoreCase))
        {
            var payload = command.StartsWith("addlogincmd ", StringComparison.OrdinalIgnoreCase)
                ? command.Substring("addlogincmd ".Length)
                : command.Substring("alc ".Length);

            if (!string.IsNullOrWhiteSpace(source.CharacterName))
            {
                _loginCommandsService.AppendCharacterCommand(
                    source.AccountName, source.ServerName, source.CharacterName, payload);
            }

            return;
        }

        if (command.StartsWith("addlogincmdglobal ", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("alcg ", StringComparison.OrdinalIgnoreCase))
        {
            var payload = command.StartsWith("addlogincmdglobal ", StringComparison.OrdinalIgnoreCase)
                ? command.Substring("addlogincmdglobal ".Length)
                : command.Substring("alcg ".Length);

            _loginCommandsService.AppendGlobalCommand(payload);
            return;
        }

        if (string.Equals(command, "disablewindowposition", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "dwp", StringComparison.OrdinalIgnoreCase))
        {
            _windowPlacement.DisableWindowPosition();
            return;
        }

        if (string.Equals(command, "lockwindowposition", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "lwp", StringComparison.OrdinalIgnoreCase))
        {
            _windowPlacement.LockWindowPosition(source);
            return;
        }

        if (string.Equals(command, "unlockwindowposition", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command, "ulwp", StringComparison.OrdinalIgnoreCase))
        {
            _windowPlacement.UnlockWindowPosition();
        }
    }

    private static void EnqueueInbound(int processId, string commandString)
    {
        var cmd = new GameCommand(DateTime.UtcNow, commandString);
        GameCommandFile.WriteInboundCommands(processId, new List<GameCommand> { cmd }, DateTime.UtcNow);
    }

    private async Task KillProcessAsync(int processId)
    {
        try
        {
            if (!IsProcessRunning(processId))
                return;

            using var proc = Process.GetProcessById(processId);
            proc.Kill(entireProcessTree: false);
            _logger.LogInformation("Channel relay killed PID {Pid}", processId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Channel relay failed to kill PID {Pid}", processId);
        }

        await Task.CompletedTask;
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
