using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Launch;

namespace ShadowFilter.Session;

internal sealed class DefaultFirstCharacterManager
{
    private readonly LoginCharacterTools _loginCharacterTools;
    private readonly CharacterSelectTicker _ticker = new();
    private string _zoneName = string.Empty;
    private string _serverName = string.Empty;
    private Func<LaunchInfo>? _getLaunchInfo;

    public DefaultFirstCharacterManager(LoginCharacterTools loginCharacterTools)
    {
        _loginCharacterTools = loginCharacterTools;
    }

    public void BindLaunchInfoProvider(Func<LaunchInfo> provider) => _getLaunchInfo = provider;

    public void OnServerDispatch(NetworkMessageEventArgs e)
    {
        if (e.Message.Type == 0xF658)
            _zoneName = Convert.ToString(e.Message["zonename"]) ?? string.Empty;

        if (e.Message.Type == 0xF7E1)
        {
            _serverName = Convert.ToString(e.Message["server"]) ?? string.Empty;
            if (!string.IsNullOrEmpty(GameRepo.Game.Server))
                _serverName = GameRepo.Game.Server;
        }

        if (e.Message.Type == 0xF7EA)
            TryStartDefaultSelect();
    }

    public void OnCommandLineText(ChatParserInterceptEventArgs e)
    {
        var lower = e.Text.ToLowerInvariant();

        if (lower.StartsWith("/tf dlc set"))
        {
            try
            {
                var name = CoreManager.Current.CharacterFilter.Name ?? string.Empty;
                DefaultFirstCharacterLoader.SetDefaultFirstCharacter(
                    new DefaultFirstCharacter(_serverName, _zoneName, name));
                ChatText.Write("Default Login Character set to: " + name);
            }
            catch { }

            e.Eat = true;
        }
        else if (lower == "/tf dlc clear")
        {
            DefaultFirstCharacterLoader.DeleteDefaultFirstCharacter(_serverName, _zoneName);
            ChatText.Write("Default Login Character cleared");
            e.Eat = true;
        }
    }

    private void TryStartDefaultSelect()
    {
        if (ParallelFilterGuard.IsThwargFilterLoaded())
        {
            PluginLog.Info(nameof(DefaultFirstCharacterManager),
                "Deferring default-first-character select to ThwargFilter (both filters loaded)");
            return;
        }

        var launchInfo = _getLaunchInfo?.Invoke() ?? new LaunchInfo();
        if (launchInfo.IsValid &&
            launchInfo.HasValidCharacterName &&
            string.Equals(launchInfo.ServerName, _serverName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        PluginLog.Info(nameof(DefaultFirstCharacterManager),
            $"Starting default-first-character path for server={_serverName}, zone={_zoneName}");

        _ticker.Start(() =>
        {
            foreach (var character in DefaultFirstCharacterLoader.DefaultFirstCharacters)
            {
                if (character.ZoneId == _zoneName && character.Server == _serverName)
                {
                    PluginLog.Info(nameof(DefaultFirstCharacterManager),
                        $"Logging in default character {character.CharacterName}");
                    _loginCharacterTools.LoginCharacter(character.CharacterName);
                    break;
                }
            }
        });
    }
}
