using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Session;

namespace ShadowFilter.Commands;

internal sealed class LoginCommandExecutor
{
    private const string LogCategory = nameof(LoginCommandExecutor);
    private static readonly TimeSpan NameWait = TimeSpan.FromSeconds(8);

    private bool _freshLogin;
    private bool _awaitingName;
    private LoginCommands _commands = new();
    private DateTime _loginCompleteTime = DateTime.MaxValue;
    private DateTime _nameDeadline = DateTime.MaxValue;
    private bool _renderHooked;
    private string _accountName = string.Empty;
    private string _serverName = string.Empty;
    private string _launchCharacter = string.Empty;

    public void OnClientDispatch(
        NetworkMessageEventArgs e,
        string accountName,
        string serverName,
        string launchFileCharacterName)
    {
        if (e.Message.Type == 0xF7C8)
            _freshLogin = true;

        if (!_freshLogin ||
            e.Message.Type != 0xF7B1 ||
            Convert.ToInt32(e.Message["action"]) != 0xA1)
        {
            return;
        }

        _freshLogin = false;
        _accountName = accountName ?? string.Empty;
        _serverName = serverName ?? string.Empty;
        _launchCharacter = launchFileCharacterName ?? string.Empty;
        _loginCompleteTime = DateTime.Now;
        _nameDeadline = DateTime.Now.Add(NameWait);
        _awaitingName = true;
        _commands = new LoginCommands();

        if (!_renderHooked)
        {
            CoreManager.Current.RenderFrame += OnRenderFrame;
            _renderHooked = true;
        }
    }

    private void OnRenderFrame(object sender, EventArgs e)
    {
        try
        {
            if (_awaitingName)
            {
                // Prefer the in-world name. Decal reports "LoginNotComplete" for a
                // short time after 0xF7B1. Use the launch-file name only if that
                // name never arrives.
                var deadlineReached = DateTime.Now >= _nameDeadline;
                var loaded = TryLoadCommands(deadlineReached);
                if (!loaded && !deadlineReached)
                    return;

                _awaitingName = false;
                if (!loaded)
                {
                    PluginLog.Info(LogCategory,
                        $"Login commands skipped: character name not ready for {_serverName}/{_accountName}");
                    Detach();
                    return;
                }

                if (_commands.Commands.Count == 0)
                {
                    Detach();
                    return;
                }
            }

            if (DateTime.Now < _loginCompleteTime.AddMilliseconds(_commands.WaitMilliseconds))
                return;

            if (_commands.Commands.Count == 0)
            {
                PluginLog.Info(LogCategory, "Login commands complete");
                Detach();
                return;
            }

            var cmd = _commands.Commands.Dequeue();
            PluginLog.Info(LogCategory, $"Dispatching login command: {cmd}");
            DecalProxy.DispatchChatToBoxWithPluginIntercept(cmd);
        }
        catch (Exception ex)
        {
            OomGuard.DieIfOom(ex);
            PluginLog.Error(LogCategory, "Login command dispatch failed", ex);
            Detach();
        }
    }

    private bool TryLoadCommands(bool allowLaunchFallback)
    {
        var characterName = ResolveCharacterName(allowLaunchFallback);
        if (string.IsNullOrEmpty(characterName))
            return false;

        _commands = LoginCommandLoader.ReadCombined(_accountName, _serverName, characterName);
        if (_commands.Commands.Count == 0)
        {
            PluginLog.Info(LogCategory,
                $"No login commands for {_serverName}/{_accountName}/{characterName}");
            return true;
        }

        PluginLog.Info(LogCategory,
            $"Loaded {_commands.Commands.Count} login command(s) for {characterName}, wait={_commands.WaitMilliseconds}ms");
        return true;
    }

    private string ResolveCharacterName(bool allowLaunchFallback)
    {
        var live = CharacterFilterTools.SafeCharacterName();
        if (IsUsableCharacterName(live))
            return live;

        if (!allowLaunchFallback)
            return string.Empty;

        return IsUsableCharacterName(_launchCharacter) ? _launchCharacter : string.Empty;
    }

    private static bool IsUsableCharacterName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !string.Equals(name, "None", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, "any", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, "LoginNotComplete", StringComparison.OrdinalIgnoreCase);

    private void Detach()
    {
        if (!_renderHooked)
            return;

        CoreManager.Current.RenderFrame -= OnRenderFrame;
        _renderHooked = false;
    }
}
