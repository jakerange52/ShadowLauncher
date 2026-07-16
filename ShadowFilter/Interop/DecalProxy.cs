using System.Runtime.InteropServices;
using Decal.Adapter;

namespace ShadowFilter.Interop;

internal static class DecalProxy
{
    [DllImport("Decal.dll")]
    private static extern int DispatchOnChatCommand(ref IntPtr str, int target);

    /// <summary>
    /// Mag-Tools style: try plugin intercept first, then InvokeChatParser.
    /// </summary>
    public static void DispatchChatToBoxWithPluginIntercept(string cmd)
    {
        var bstr = Marshal.StringToBSTR(cmd);
        try
        {
            var eaten = (DispatchOnChatCommand(ref bstr, 1) & 0x1) > 0;
            if (!eaten)
                CoreManager.Current.Actions.InvokeChatParser(cmd);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }
}
