using Decal.Adapter;
using ShadowFilter.Launch;

namespace ShadowFilter.Session;

/// <summary>
/// Auto-selects the character named in the ShadowLauncher launch file on 0xF7EA.
/// Defers to ThwargFilter when both filters are loaded (ShadowLauncher dual-writes its launch files).
/// </summary>
internal sealed class CharacterSelectManager
{
    private readonly LoginCharacterTools _loginCharacterTools;
    private readonly CharacterSelectTicker _ticker = new();
    private string _serverName = string.Empty;
    private Func<LaunchInfo>? _getLaunchInfo;

    public CharacterSelectManager(LoginCharacterTools loginCharacterTools)
    {
        _loginCharacterTools = loginCharacterTools;
    }

    public void BindLaunchInfoProvider(Func<LaunchInfo> provider) => _getLaunchInfo = provider;

    public void OnServerDispatch(NetworkMessageEventArgs e)
    {
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
        if (ParallelFilterGuard.IsThwargFilterLoaded())
        {
            PluginLog.Info(nameof(CharacterSelectManager),
                "Deferring character select to ThwargFilter (both filters loaded)");
            return;
        }

        var launchInfo = _getLaunchInfo?.Invoke() ?? new LaunchInfo();
        if (!launchInfo.IsValid)
        {
            PluginLog.Info(nameof(CharacterSelectManager), "Skipping launch-file select: no valid launch file");
            return;
        }

        if (!launchInfo.HasValidCharacterName)
        {
            PluginLog.Info(nameof(CharacterSelectManager), "Skipping launch-file select: no character in launch file");
            return;
        }

        if (!string.Equals(launchInfo.ServerName, _serverName, StringComparison.OrdinalIgnoreCase))
        {
            PluginLog.Info(nameof(CharacterSelectManager),
                $"Skipping launch-file select: server mismatch (launch={launchInfo.ServerName}, packet={_serverName})");
            return;
        }

        PluginLog.Info(nameof(CharacterSelectManager),
            $"Starting 4-tick char select for {launchInfo.CharacterName} on {launchInfo.ServerName}");

        _ticker.Start(() =>
        {
            if (_loginCharacterTools.LoginCharacter(launchInfo.CharacterName))
            {
                PluginLog.Info(nameof(CharacterSelectManager),
                    $"Logged in character {launchInfo.CharacterName}");
                Monitoring.HeartbeatWriter.RecordCharacterName(launchInfo.CharacterName);
            }
            else
            {
                PluginLog.Warn(nameof(CharacterSelectManager),
                    $"LoginCharacter failed for {launchInfo.CharacterName}");
            }
        });
    }
}
