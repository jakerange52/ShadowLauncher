using Decal.Adapter;
using ShadowFilter.Commands;
using ShadowFilter.Launch;
using ShadowFilter.Session;
using System.Diagnostics;
using System.Reflection;

namespace ShadowFilter;

[FriendlyName("ShadowFilter")]
public sealed class FilterCore : FilterBase
{
    private readonly LoginCharacterTools _loginCharacterTools = new();
    private readonly CharacterSelectManager _characterSelectManager;
    private readonly DefaultFirstCharacterManager _defaultFirstCharacterManager;
    private readonly LoginNextCharacterManager _loginNextCharacterManager;
    private readonly AutoRetryLogin _autoRetryLogin = new();
    private readonly FastQuit _fastQuit = new();
    private readonly LoginCompleteMessageQueueManager _loginCompleteMessageQueueManager = new();
    private readonly LoginCommandExecutor _loginCommandExecutor = new();
    private readonly FilterCommandParser _filterCommandParser = new();
    private DateTime _lastServerDispatchUtc = DateTime.MinValue;
    private LaunchInfo? _cachedLaunchInfo;

    public FilterCore()
    {
        _characterSelectManager = new CharacterSelectManager(_loginCharacterTools);
        _defaultFirstCharacterManager = new DefaultFirstCharacterManager(_loginCharacterTools);
        _loginNextCharacterManager = new LoginNextCharacterManager(_loginCharacterTools);
    }

    protected override void Startup()
    {
        Paths.FilterPaths.EnsureDataFoldersExist();
        PluginLog.CleanOldLogs();

        var assembly = Assembly.GetExecutingAssembly();
        var fileVer = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion;
        PluginLog.Info(nameof(FilterCore),
            $"ShadowFilter starting (AssemblyVer={assembly.GetName().Version}, FileVer={fileVer}, Log={PluginLog.LogFilePath})");

        Launch.LaunchFileReader.DeleteStaleLaunchFiles();
        Monitoring.HeartbeatWriter.BindServerDispatchTracker(() => _lastServerDispatchUtc);
        Monitoring.HeartbeatWriter.SetCommandParser(_filterCommandParser);
        _characterSelectManager.BindLaunchInfoProvider(GetLaunchInfo);
        _defaultFirstCharacterManager.BindLaunchInfoProvider(GetLaunchInfo);

        ServerDispatch += OnServerDispatch;
        ClientDispatch += OnClientDispatch;
        CommandLineText += OnCommandLineText;
        WindowMessage += OnWindowMessage;
    }

    protected override void Shutdown()
    {
        PluginLog.Info(nameof(FilterCore), "ShadowFilter shutting down");

        ServerDispatch -= OnServerDispatch;
        ClientDispatch -= OnClientDispatch;
        CommandLineText -= OnCommandLineText;
        WindowMessage -= OnWindowMessage;
        Monitoring.HeartbeatWriter.StopAndDelete();
    }

    private LaunchInfo GetLaunchInfo()
    {
        if (_cachedLaunchInfo?.IsValid == true)
            return _cachedLaunchInfo;

        var account = GameRepo.Game.Account;
        var server = GameRepo.Game.Server;
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(server))
        {
            try
            {
                account = CoreManager.Current.CharacterFilter.AccountName ?? string.Empty;
                server = CoreManager.Current.CharacterFilter.Server ?? string.Empty;
            }
            catch
            {
                return _cachedLaunchInfo ?? new LaunchInfo();
            }
        }

        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(server))
            return _cachedLaunchInfo ?? new LaunchInfo();

        var info = LaunchFileReader.Read(server, account);
        if (info.IsValid)
        {
            _cachedLaunchInfo = info;
            if (!string.IsNullOrEmpty(info.CharacterName))
                Monitoring.HeartbeatWriter.RecordCharacterName(info.CharacterName);
        }

        return info;
    }

    private void OnServerDispatch(object sender, NetworkMessageEventArgs e)
    {
        _lastServerDispatchUtc = DateTime.UtcNow;

        if (e.Message.Type == 0xF7E1)
        {
            var serverFromPacket = Convert.ToString(e.Message["server"]) ?? string.Empty;
            if (!string.IsNullOrEmpty(serverFromPacket))
                GameRepo.Game.SetServer(serverFromPacket);

            try
            {
                var account = CoreManager.Current.CharacterFilter.AccountName;
                if (!string.IsNullOrEmpty(account))
                    GameRepo.Game.SetAccount(account);
            }
            catch { }

            var launchInfo = GetLaunchInfo();
            if (launchInfo.IsValid)
            {
                GameRepo.Game.SetServer(launchInfo.ServerName);
                GameRepo.Game.SetAccount(launchInfo.AccountName);
            }
        }

        _autoRetryLogin.OnServerDispatch(e);
        _loginCharacterTools.OnServerDispatch(e, GetLaunchInfo);
        _defaultFirstCharacterManager.OnServerDispatch(e);
        _characterSelectManager.OnServerDispatch(e);
        _loginNextCharacterManager.OnServerDispatch(e);
    }

    private void OnClientDispatch(object sender, NetworkMessageEventArgs e)
    {
        _autoRetryLogin.OnClientDispatch(e);
        _loginCompleteMessageQueueManager.OnClientDispatch(e);

        var launchInfo = GetLaunchInfo();
        var account = launchInfo.IsValid ? launchInfo.AccountName : GameRepo.Game.Account;
        var server = launchInfo.IsValid ? launchInfo.ServerName : GameRepo.Game.Server;
        var launchCharacter = launchInfo.IsValid && launchInfo.HasValidCharacterName
            ? launchInfo.CharacterName
            : string.Empty;

        _loginCommandExecutor.OnClientDispatch(e, account, server, launchCharacter);
    }

    private void OnCommandLineText(object sender, ChatParserInterceptEventArgs e)
    {
        _loginCompleteMessageQueueManager.OnCommandLineText(e);
        _defaultFirstCharacterManager.OnCommandLineText(e);
        _loginNextCharacterManager.OnCommandLineText(e);
        _filterCommandParser.OnCommandLineText(e);
    }

    private void OnWindowMessage(object sender, WindowMessageEventArgs e) =>
        _fastQuit.OnWindowMessage(e);
}
