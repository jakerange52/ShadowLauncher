namespace ShadowLauncher.Core.Models;

public class Server : IEquatable<Server>
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public EmulatorType Emulator { get; set; } = EmulatorType.ACE;
    public string Description { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public int Port { get; set; }
    public string DiscordUrl { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public bool DefaultRodat { get; set; }
    public bool SecureLogon { get; set; }
    public string PublishedStatus { get; set; } = string.Empty;

    /// <summary>
    /// The ID of the DatSet this server requires (e.g. "dark-majesty").
    /// Null or empty means the server uses the standard end-of-retail DATs
    /// and no special DAT management is needed.
    /// </summary>
    public string? DatSetId { get; set; }

    /// <summary>
    /// Optional local folder path to a custom DAT registry for this server.
    /// When set (Dat Developer Mode), the registry service is bypassed and
    /// DATs are loaded from this path directly.
    /// </summary>
    public string? CustomDatRegistryPath { get; set; }

    public bool IsOnline { get; set; }
    public DateTime LastStatusCheck { get; set; }

    public bool IsValid()
        => !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Hostname)
        && Port > 0 && Port <= 65535;

    public string GetDisplayName()
    {
        var status = IsOnline ? "●" : "○";
        return $"{status} {Name} [{Emulator}]";
    }

    public bool Equals(Server? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Server);
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;
}

public enum EmulatorType
{
    ACE = 0,
    GDLE = 1
}
