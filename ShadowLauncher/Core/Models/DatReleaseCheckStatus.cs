namespace ShadowLauncher.Core.Models;

public enum DatReleaseCheckStatus
{
    NotApplicable,
    UpToDate,
    Stale,
    RemoteUnavailable,
}

public sealed record DatReleaseCheckResult(
    DatReleaseCheckStatus Status,
    string ServerId,
    string ServerName,
    string? CachedTag,
    string? RemoteTag);
