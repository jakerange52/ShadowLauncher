using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Monitoring;
using ShadowFilter.Session;

namespace ShadowFilter.Commands;

internal sealed class FilterCommandParser
{
    private delegate void ExecuteCommandHandler(string command);

    private sealed class CommandEntry
    {
        public string Command { get; }
        public ExecuteCommandHandler? Handler { get; }
        public string? Help { get; }

        public CommandEntry(string cmd, ExecuteCommandHandler? handler, string? help)
        {
            Command = cmd;
            Handler = handler;
            Help = help;
        }
    }

    private readonly List<CommandEntry> _handlers = new();
    private readonly FilterCommandExecutor _executor;
    private readonly Dictionary<string, int> _teams = new(StringComparer.OrdinalIgnoreCase);

    private const string CmdVersion = "version";
    private const string CmdHelp = "help";
    private const string CmdHelp2 = "?";
    private const string CmdHelp3 = "/?";
    private const string CmdBroadcast = "broadcast ";
    private const string CmdBroadcast2 = "bc ";
    private const string CmdCreateTeam = "createteam ";
    private const string CmdCreateTeam2 = "ct ";
    private const string CmdShowTeams = "showteams";
    private const string CmdShowTeams2 = "st";
    private const string CmdJoinTeam = "jointeam ";
    private const string CmdJoinTeam2 = "jt ";
    private const string CmdLeaveTeam = "leaveteam ";
    private const string CmdLeaveTeam2 = "lt ";
    private const string CmdSetWindowTitle = "swt ";
    private const string CmdKillClient = "killclient";
    private const string CmdKillClient2 = "kc";
    private const string CmdKillAllClients = "killallclients";
    private const string CmdKillAllClients2 = "kac";
    private const string CmdAddLoginCmd = "addlogincmd ";
    private const string CmdAddLoginCmd2 = "alc ";
    private const string CmdAddLoginCmdGlobal = "addlogincmdglobal ";
    private const string CmdAddLoginCmdGlobal2 = "alcg ";
    private const string CmdDisableWindowPosition = "disablewindowposition";
    private const string CmdDisableWindowPosition2 = "dwp";
    private const string CmdLockWindowPosition = "lockwindowposition";
    private const string CmdLockWindowPosition2 = "lwp";
    private const string CmdUnlockWindowPosition = "unlockwindowposition";
    private const string CmdUnlockWindowPosition2 = "ulwp";

    public FilterCommandParser(FilterCommandExecutor executor)
    {
        _executor = executor;
        _handlers.Add(new CommandEntry(CmdVersion, VersionHandler, "Display assembly version info"));
        _handlers.Add(new CommandEntry(CmdHelp, HelpHandler, "List all /tf commands"));
        _handlers.Add(new CommandEntry(CmdHelp2, HelpHandler, null));
        _handlers.Add(new CommandEntry(CmdHelp3, HelpHandler, null));
        _handlers.Add(new CommandEntry(CmdBroadcast, BroadcastHandler, "Broadcast command to other clients"));
        _handlers.Add(new CommandEntry(CmdBroadcast2, BroadcastHandler, null));
        _handlers.Add(new CommandEntry(CmdCreateTeam, CreateTeamHandler, "Create team of characters"));
        _handlers.Add(new CommandEntry(CmdCreateTeam2, CreateTeamHandler, null));
        _handlers.Add(new CommandEntry(CmdShowTeams, ShowTeamsHandler, "Show all teams"));
        _handlers.Add(new CommandEntry(CmdShowTeams2, ShowTeamsHandler, null));
        _handlers.Add(new CommandEntry(CmdJoinTeam, JoinTeamHandler, "Join a team"));
        _handlers.Add(new CommandEntry(CmdJoinTeam2, JoinTeamHandler, null));
        _handlers.Add(new CommandEntry(CmdLeaveTeam, LeaveTeamHandler, "Leave a team"));
        _handlers.Add(new CommandEntry(CmdLeaveTeam2, LeaveTeamHandler, null));
        _handlers.Add(new CommandEntry(CmdSetWindowTitle, SetWindowTitleHandler, "Set window title"));
        _handlers.Add(new CommandEntry(CmdKillClient, KillClientHandler, "Kill current client"));
        _handlers.Add(new CommandEntry(CmdKillClient2, KillClientHandler, null));
        _handlers.Add(new CommandEntry(CmdKillAllClients, KillAllClientsHandler, "Kill all clients"));
        _handlers.Add(new CommandEntry(CmdKillAllClients2, KillAllClientsHandler, null));
        _handlers.Add(new CommandEntry(CmdAddLoginCmd, AddLoginCmdHandler, "Add per-character login command"));
        _handlers.Add(new CommandEntry(CmdAddLoginCmd2, AddLoginCmdHandler, null));
        _handlers.Add(new CommandEntry(CmdAddLoginCmdGlobal, AddLoginCmdGlobalHandler, "Add global login command"));
        _handlers.Add(new CommandEntry(CmdAddLoginCmdGlobal2, AddLoginCmdGlobalHandler, null));
        _handlers.Add(new CommandEntry(CmdDisableWindowPosition, DisableWindowPositionHandler, "Disable window position management"));
        _handlers.Add(new CommandEntry(CmdDisableWindowPosition2, DisableWindowPositionHandler, null));
        _handlers.Add(new CommandEntry(CmdLockWindowPosition, LockWindowPositionHandler, "Lock window positions"));
        _handlers.Add(new CommandEntry(CmdLockWindowPosition2, LockWindowPositionHandler, null));
        _handlers.Add(new CommandEntry(CmdUnlockWindowPosition, UnlockWindowPositionHandler, "Unlock window positions"));
        _handlers.Add(new CommandEntry(CmdUnlockWindowPosition2, UnlockWindowPositionHandler, null));
    }

    public string GetTeamList() => string.Join(",", _teams.Keys);

    public void ExecuteCommandFromLauncher(string command)
    {
        if (TryPrefix(command, CmdJoinTeam, out var joinCmd) ||
            TryPrefix(command, CmdJoinTeam2, out joinCmd))
        {
            JoinTeamHandler(joinCmd);
            return;
        }

        if (TryPrefix(command, CmdLeaveTeam, out var leaveCmd) ||
            TryPrefix(command, CmdLeaveTeam2, out leaveCmd))
        {
            LeaveTeamHandler(leaveCmd);
            return;
        }

        _executor.ExecuteCommand(command);
    }

    public void OnCommandLineText(ChatParserInterceptEventArgs e)
    {
        if (TryPrefix(e.Text, "/tf log ", out var logMsg))
        {
            PluginLog.WriteInfo(logMsg);
            e.Eat = true;
            return;
        }

        foreach (var entry in _handlers)
        {
            if (entry.Handler == null)
                continue;

            var prefix = "/tf " + entry.Command;
            if (!TryPrefix(e.Text, prefix, out var commandString))
                continue;

            entry.Handler(commandString);
            e.Eat = true;
            break;
        }
    }

    private void VersionHandler(string _)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var msg = $"ShadowFilter, AssemblyVer: {assembly.GetName().Version}, FileVer: {FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion}";
        ChatText.Write("Version: " + msg);
        PluginLog.WriteInfo(msg);
    }

    private void HelpHandler(string _)
    {
        var lines = _handlers
            .Where(h => h.Help != null)
            .Select(h => $"{h.Command.Trim()}: {h.Help}");
        ChatText.Write("Commands: " + string.Join("\n", lines));
    }

    private void BroadcastHandler(string command)
    {
        if (!string.IsNullOrEmpty(command))
        {
            HeartbeatWriter.SendCommand(CmdBroadcast + command);
            HeartbeatWriter.SendAndReceiveImmediately();
        }
    }

    private void CreateTeamHandler(string command)
    {
        if (!string.IsNullOrEmpty(command))
        {
            HeartbeatWriter.SendCommand(CmdCreateTeam + command);
            HeartbeatWriter.SendAndReceiveImmediately();
        }
    }

    private void ShowTeamsHandler(string _) => ChatText.Write("Teams: " + GetTeamList());

    private void JoinTeamHandler(string command)
    {
        foreach (var team in command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!_teams.ContainsKey(team))
                _teams.Add(team, 1);
        }
    }

    private void LeaveTeamHandler(string command)
    {
        foreach (var team in command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            _teams.Remove(team);
    }

    private void SetWindowTitleHandler(string command)
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var process = System.Diagnostics.Process.GetProcessById(pid);
        var pattern = command
            .Replace("%ACCOUNT%", GameRepo.Game.Account)
            .Replace("%SERVER%", GameRepo.Game.Server)
            .Replace("%CHARACTER%", GameRepo.Game.Character);
        WindowTools.SetWindowText(process.MainWindowHandle, pattern);
    }

    private void KillClientHandler(string _) => SendChannel(CmdKillClient);
    private void KillAllClientsHandler(string _) => SendChannel(CmdKillAllClients);
    private void AddLoginCmdHandler(string command) => SendChannel(CmdAddLoginCmd + command);
    private void AddLoginCmdGlobalHandler(string command) => SendChannel(CmdAddLoginCmdGlobal + command);
    private void DisableWindowPositionHandler(string _) { SendChannel(CmdDisableWindowPosition); ChatText.Write("Window positions disabled."); }
    private void LockWindowPositionHandler(string _) { SendChannel(CmdLockWindowPosition); ChatText.Write("Window positions locked."); }
    private void UnlockWindowPositionHandler(string _) { SendChannel(CmdUnlockWindowPosition); ChatText.Write("Window positions unlocked."); }

    private void SendChannel(string command)
    {
        HeartbeatWriter.SendCommand(command);
        HeartbeatWriter.SendAndReceiveImmediately();
    }

    private static bool TryPrefix(string line, string prefix, out string command)
    {
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            command = line.Length > prefix.Length ? line.Substring(prefix.Length) : string.Empty;
            return true;
        }

        command = string.Empty;
        return false;
    }
}

internal static class WindowTools
{
    [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SetWindowText")]
    private static extern bool SetWindowTextNative(IntPtr hWnd, string text);

    public static void SetWindowText(IntPtr hwnd, string text)
    {
        if (hwnd != IntPtr.Zero)
            SetWindowTextNative(hwnd, text);
    }
}
