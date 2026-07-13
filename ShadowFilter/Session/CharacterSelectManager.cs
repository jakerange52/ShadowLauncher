using Decal.Adapter;
using ShadowFilter.Launch;

namespace ShadowFilter.Session;

/// <summary>
/// Auto-selects the character named in the ShadowLauncher launch file on 0xF7EA.
/// </summary>
internal sealed class CharacterSelectManager
{
    private readonly LoginCharacterTools _loginCharacterTools;
    private readonly CharacterSelectTicker _ticker = new();
    private string _zoneName = string.Empty;
    private string _serverName = string.Empty;
    private Func<LaunchInfo>? _getLaunchInfo;

    public CharacterSelectManager(LoginCharacterTools loginCharacterTools)
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
            var launchInfo = _getLaunchInfo?.Invoke() ?? new LaunchInfo();
            if (!string.IsNullOrEmpty(launchInfo.ServerName))
                _serverName = launchInfo.ServerName;
        }

        if (e.Message.Type == 0xF7EA)
            TryStartLaunchFileSelect();
    }

    private void TryStartLaunchFileSelect()
    {
        var launchInfo = _getLaunchInfo?.Invoke() ?? new LaunchInfo();
        if (!launchInfo.IsValid ||
            !launchInfo.HasValidCharacterName ||
            !launchInfo.IsRecentLaunch ||
            !string.Equals(launchInfo.ServerName, _serverName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ticker.Start(() =>
        {
            if (_loginCharacterTools.LoginCharacter(launchInfo.CharacterName))
                Monitoring.HeartbeatWriter.RecordCharacterName(launchInfo.CharacterName);
        });
    }
}
