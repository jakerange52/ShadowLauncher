using Decal.Adapter;
using ShadowFilter.Interop;
using ShadowFilter.Launch;

namespace ShadowFilter.Session;

internal sealed class LoginCharacterTools
{
    private readonly List<GameCharacter> _characters = new();
    private bool _characterListWritten;
    private int _characterSlots;
    private string? _trackedCharacterName;

    public void OnServerDispatch(NetworkMessageEventArgs e, Func<LaunchInfo> getLaunchInfo)
    {
        if (e.Message.Type == 0xF658)
        {
            GameRepo.Game.SetZoneName(Convert.ToString(e.Message["zonename"]) ?? string.Empty);
            _characterSlots = Convert.ToInt32(e.Message["slotCount"]);
            _characters.Clear();

            var charactersStruct = e.Message.Struct("characters");
            for (var i = 0; i < charactersStruct.Count; i++)
            {
                var entry = charactersStruct.Struct(i);
                _characters.Add(new GameCharacter
                {
                    Name = Convert.ToString(entry["name"]) ?? string.Empty
                });
            }

            _characters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        }

        if (e.Message.Type == 0xF7E1)
        {
            var server = Convert.ToString(e.Message["server"]) ?? string.Empty;
            if (!string.IsNullOrEmpty(server))
                GameRepo.Game.SetServer(server);

            var launchInfo = getLaunchInfo();
            if (launchInfo.IsValid)
            {
                if (!string.IsNullOrEmpty(launchInfo.ServerName))
                    GameRepo.Game.SetServer(launchInfo.ServerName);

                if (!string.IsNullOrEmpty(launchInfo.AccountName))
                    GameRepo.Game.SetAccount(launchInfo.AccountName);

                Monitoring.HeartbeatWriter.EnsureStarted();
            }
        }

        if (!_characterListWritten &&
            !string.IsNullOrEmpty(GameRepo.Game.Server) &&
            !string.IsNullOrEmpty(GameRepo.Game.ZoneName) &&
            _characters.Count > 0)
        {
            var launchInfo = getLaunchInfo();
            if (launchInfo.IsValid)
            {
                CharacterListWriter.Write(launchInfo.ServerName, launchInfo.AccountName, GameRepo.Game.ZoneName, _characters);
                Monitoring.HeartbeatWriter.EnsureStarted();
                _characterListWritten = true;
            }
        }
    }

    /// <summary>
    /// Call from login-complete (not per-packet) — CharacterFilter COM on every
    /// ServerDispatch was thrashing the client under load.
    /// </summary>
    public void TrackCharacterNameIfChanged()
    {
        var currentName = CharacterFilterTools.SafeCharacterName();
        if (string.IsNullOrEmpty(currentName) ||
            string.Equals(currentName, "LoginNotComplete", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(currentName, _trackedCharacterName, StringComparison.Ordinal))
            return;

        _trackedCharacterName = currentName;
        GameRepo.Game.SetCharacter(currentName);
        Monitoring.HeartbeatWriter.RecordCharacterName(currentName);
    }

    public bool LoginCharacter(string name)
    {
        for (var i = 0; i < _characters.Count; i++)
        {
            if (string.Equals(_characters[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return LoginByIndex(i);
        }

        return false;
    }

    public bool LoginByIndex(int index)
    {
        if (index >= _characters.Count || _characterSlots <= 0)
            return false;

        const int xPixelOffset = 121;
        const int yTopOfBox = 209;
        const int yBottomOfBox = 532;

        var characterNameSize = (yBottomOfBox - yTopOfBox) / (float)_characterSlots;
        var yOffset = (int)(yTopOfBox + (characterNameSize / 2) + (characterNameSize * index));

        PostMessageTools.SendMouseClick(xPixelOffset, yOffset);
        PostMessageTools.SendMouseClick(0x015C, 0x0185);
        return true;
    }
}
