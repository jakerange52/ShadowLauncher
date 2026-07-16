using Decal.Adapter;
using ShadowFilter.Interop;

namespace ShadowFilter.Session;

internal sealed class FastQuit
{
    public void OnWindowMessage(WindowMessageEventArgs e)
    {
        try
        {
            var name = CoreManager.Current.CharacterFilter.Name;
            if (!string.IsNullOrEmpty(name) && name != "LoginNotComplete")
                return;

            if (e.Msg == 0x0100 && e.WParam == 0x0000001B && e.LParam == 0x00010001)
                PostMessageTools.ClickYes();
        }
        catch { }
    }
}
