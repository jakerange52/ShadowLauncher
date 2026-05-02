using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Servers;

public class ServerService : IServerService
{
    private readonly IRepository<Server> _repository;
    private readonly ILogger<ServerService> _logger;

    public event EventHandler? ServersChanged;

    public ServerService(
        IRepository<Server> repository,
        ILogger<ServerService> logger)
    {
        _repository = repository;
        _logger = logger;

        // Forward file-change notifications from the repository through the service interface
        // so consumers don't need to depend on the concrete repository type.
        if (repository is Infrastructure.Persistence.ServerFileRepository repo)
            repo.ServersChanged += (s, e) => ServersChanged?.Invoke(s, e);
    }

    public Task<Server?> GetServerAsync(string serverId) => _repository.GetByIdAsync(serverId);
    public Task<IEnumerable<Server>> GetAllServersAsync() => _repository.GetAllAsync();

    public async Task<IEnumerable<Server>> GetActiveServersAsync()
        => await _repository.FindAsync(s => s.IsOnline);

    public async Task<Server> CreateServerAsync(Server server)
    {
        server.Id = server.Name.ToLowerInvariant();
        await _repository.AddAsync(server);
        _logger.LogInformation("Server created: {Name} ({Hostname}:{Port})", server.Name, server.Hostname, server.Port);
        return server;
    }

    public Task UpdateServerAsync(Server server) => _repository.UpdateAsync(server);
    public Task DeleteServerAsync(string serverId) => _repository.DeleteAsync(serverId);

    public async Task<bool> CheckServerStatusAsync(string serverId)
    {
        var server = await _repository.GetByIdAsync(serverId);
        if (server is null) return false;

        bool isOnline;
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(server.Hostname);
            isOnline = addresses.Length > 0 &&
                       await IsUdpServerUpAsync(addresses[0].ToString(), server.Port);
        }
        catch
        {
            isOnline = false;
        }

        // Mutate IsOnline in-place on the cached object so any UI-bound reference
        // fires INotifyPropertyChanged without needing a full collection reload.
        server.IsOnline = isOnline;
        server.LastStatusCheck = DateTime.UtcNow;
        await _repository.UpdateAsync(server);
        return server.IsOnline;
    }

    /// <summary>
    /// Sends an AC-protocol UDP connect/login probe and waits for any response.
    /// This is how ThwargLauncher detects server status — works even when ICMP is blocked.
    /// </summary>
    private static async Task<bool> IsUdpServerUpAsync(string address, int port, int timeoutMs = 3000)
    {
        using var udpClient = new System.Net.Sockets.UdpClient();
        try
        {
            udpClient.Connect(address, port);

            // AC protocol login probe packet (matches ThwargLauncher's Packet.MakeLoginPacket)
            byte[] loginPacket =
            [
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00,
                0x93, 0x00, 0xD0, 0x05, 0x00, 0x00, 0x00, 0x00,
                0x40, 0x00, 0x00, 0x00, 0x04, 0x00, 0x31, 0x38,
                0x30, 0x32, 0x00, 0x00, 0x34, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x3E, 0xB8, 0xA8, 0x58, 0x1C, 0x00, 0x61, 0x63,
                0x73, 0x65, 0x72, 0x76, 0x65, 0x72, 0x74, 0x72,
                0x61, 0x63, 0x6B, 0x65, 0x72, 0x3A, 0x6A, 0x6A,
                0x39, 0x68, 0x32, 0x36, 0x68, 0x63, 0x73, 0x67,
                0x67, 0x63, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            ];

            await udpClient.SendAsync(loginPacket, loginPacket.Length);

            var receiveTask = udpClient.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(timeoutMs));
            return completed == receiveTask;
        }
        catch
        {
            return false;
        }
    }

    public async Task RefreshAllServerStatusAsync()
    {
        var servers = await _repository.GetAllAsync();
        var tasks = servers.Select(s => CheckServerStatusAsync(s.Id));
        await Task.WhenAll(tasks);
        _logger.LogDebug("Refreshed status for all servers");
    }

    }
