using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Interfaces;
using ShadowLauncher.Core.Models;

namespace ShadowLauncher.Services.Servers;

public class ServerService : IServerService
{
    private readonly IRepository<Server> _repository;
    private readonly IRepository<Account> _accountRepository;
    private readonly IEventAggregator _events;
    private readonly ILogger<ServerService> _logger;

    public ServerService(
        IRepository<Server> repository,
        IRepository<Account> accountRepository,
        IEventAggregator events,
        ILogger<ServerService> logger)
    {
        _repository = repository;
        _accountRepository = accountRepository;
        _events = events;
        _logger = logger;
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

        try
        {
            // Resolve hostname first
            var addresses = await System.Net.Dns.GetHostAddressesAsync(server.Hostname);
            if (addresses.Length == 0)
            {
                server.IsOnline = false;
            }
            else
            {
                // Send a UDP login probe packet (same approach as ThwargLauncher).
                // AC emulator servers respond to this even when ICMP is blocked.
                server.IsOnline = await IsUdpServerUpAsync(addresses[0].ToString(), server.Port);
            }
        }
        catch
        {
            server.IsOnline = false;
        }

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
        _logger.LogInformation("Refreshed status for all servers");
    }

    public async Task AssociateServerWithAccountAsync(string accountId, string serverId)
    {
        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account is null) return;
        if (!account.ServerIds.Contains(serverId))
        {
            account.ServerIds.Add(serverId);
            await _accountRepository.UpdateAsync(account);
        }
    }

    public async Task RemoveServerFromAccountAsync(string accountId, string serverId)
    {
        var account = await _accountRepository.GetByIdAsync(accountId);
        if (account is null) return;
        account.ServerIds.Remove(serverId);
        await _accountRepository.UpdateAsync(account);
    }
}
