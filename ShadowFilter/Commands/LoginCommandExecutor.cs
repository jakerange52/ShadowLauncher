using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Session;

namespace ShadowFilter.Commands;

internal sealed class LoginCommandExecutor
{
    private bool _freshLogin;
    private LoginCommands _commands = new();
    private DateTime _loginCompleteTime = DateTime.MaxValue;
    private bool _renderHooked;

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

        var characterName = CharacterFilterTools.SafeCharacterName();
        if (string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(launchFileCharacterName))
            characterName = launchFileCharacterName;

        _commands = LoginCommandLoader.ReadCombined(accountName, serverName, characterName);
        if (_commands.Commands.Count == 0)
            return;

        _loginCompleteTime = DateTime.Now;
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
            if (DateTime.Now < _loginCompleteTime.AddMilliseconds(_commands.WaitMilliseconds))
                return;

            if (_commands.Commands.Count == 0)
            {
                Detach();
                return;
            }

            var cmd = _commands.Commands.Dequeue();
            DecalProxy.DispatchChatToBoxWithPluginIntercept(cmd);
        }
        catch
        {
            Detach();
        }
    }

    private void Detach()
    {
        if (!_renderHooked)
            return;

        CoreManager.Current.RenderFrame -= OnRenderFrame;
        _renderHooked = false;
    }
}
