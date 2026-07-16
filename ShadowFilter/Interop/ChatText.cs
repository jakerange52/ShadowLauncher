using Decal.Adapter;

namespace ShadowFilter.Interop;

internal static class ChatText
{
    public static void Write(string message)
    {
        try { CoreManager.Current.Actions.AddChatText(message, 1); } catch { }
    }
}
