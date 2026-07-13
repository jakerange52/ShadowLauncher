using Decal.Adapter;

namespace ShadowFilter.Commands;

internal sealed class FilterCommandExecutor
{
    public void ExecuteCommand(string command)
    {
        Interop.DecalProxy.DispatchChatToBoxWithPluginIntercept(command);
    }
}
