using ShadowLauncher.Services.GameSessions;

namespace ShadowLauncher.Infrastructure.FileSystem;

public interface IHeartbeatReader
{
    Task<HeartbeatData?> ReadHeartbeatAsync(int processId);
}
