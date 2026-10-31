using Microsoft.Extensions.Logging;
using ShadowLauncher.Core.Models;
using ShadowLauncher.Infrastructure.Protocol.Ace;
namespace ShadowLauncher.Services.Dats;

public interface IAceDatProbeService
{
    Task<AceDatProbeOutcome> ProbeServerDatIterationsAsync(
        Server server,
        Account account,
        CancellationToken cancellationToken = default);
}

public sealed class AceDatProbeService : IAceDatProbeService
{
    private readonly IDatSetService _datSetService;
    private readonly ILogger<AceDatProbeService> _logger;

    public AceDatProbeService(IDatSetService datSetService, ILogger<AceDatProbeService> logger)
    {
        _datSetService = datSetService;
        _logger = logger;
    }

    public async Task<AceDatProbeOutcome> ProbeServerDatIterationsAsync(
        Server server,
        Account account,
        CancellationToken cancellationToken = default)
    {
        if (!DatManagedServerHelper.RequiresAceDatProbe(server))
            return AceDatProbeOutcome.Inconclusive;

        var cacheDir = _datSetService.GetLocalDatSetPathForServer(server);
        var probe = new AceDatDddProbe(_logger);
        return await probe.ProbeAsync(server, account, cacheDir, cancellationToken);
    }
}
