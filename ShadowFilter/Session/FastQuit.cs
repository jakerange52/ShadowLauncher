using Decal.Adapter;
using ShadowFilter.Interop;

namespace ShadowFilter.Session;

internal sealed class FastQuit
{
    public void OnWindowMessage(WindowMessageEventArgs e)
    {
        // Only ESC keydown — do not touch CharacterFilter on every mouse/key message.
        if (e.Msg != 0x0100 || e.WParam != 0x0000001B || e.LParam != 0x00010001)
            return;

        try
        {
            var name = CharacterFilterTools.SafeCharacterName();
            if (!string.IsNullOrEmpty(name) &&
                !string.Equals(name, "LoginNotComplete", StringComparison.OrdinalIgnoreCase))
                return;

            PostMessageTools.ClickYes();
        }
        catch { }
    }
}
