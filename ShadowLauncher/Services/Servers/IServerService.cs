using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Servers;

public interface IServerService
{
    event EventHandler? ServersChanged;

    Task<Server?> GetServerAsync(string serverId);
    Task<IEnumerable<Server>> GetAllServersAsync();
    Task<IEnumerable<Server>> GetActiveServersAsync();
    Task<Server> CreateServerAsync(Server server);
    Task UpdateServerAsync(Server server);
    Task DeleteServerAsync(string serverId);
    Task<bool> CheckServerStatusAsync(string serverId);
    Task RefreshAllServerStatusAsync();
}
