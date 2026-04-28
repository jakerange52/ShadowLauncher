using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShadowLauncher.Core.Models;

public class Server : IEquatable<Server>, INotifyPropertyChanged
{
    private bool _isOnline;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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

    /// <summary>
    /// Optional URL to a zip archive containing the DAT files for this server.
    /// Used in Dat Developer Mode as an alternative to <see cref="CustomDatRegistryPath"/>
    /// so remote developers can all pull the same DATs from a shared hosted location
    /// (e.g. a private file host, S3 bucket, or direct download link).
    /// If both are set, <see cref="CustomDatRegistryPath"/> takes priority (local wins).
    /// </summary>
    public string? CustomDatZipUrl { get; set; }

    /// <summary>
    /// True when this server was added manually via the Add Server dialog,
    /// as opposed to imported from the community server list. Only manually
    /// added servers can be edited after creation.
    /// </summary>
    public bool IsManuallyAdded { get; set; }

    /// <summary>
    /// True when this server was sourced from the beta server list.
    /// These servers display a β badge in the UI.
    /// </summary>
    public bool IsBeta { get; set; }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value) return;
            _isOnline = value;
            OnPropertyChanged();
        }
    }

    public DateTime LastStatusCheck { get; set; }

    public bool IsValid()
        => !string.IsNullOrWhiteSpace(Name)
        && !string.IsNullOrWhiteSpace(Hostname)
        && Port > 0 && Port <= 65535;

    public bool Equals(Server? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Server);
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;
}

public enum EmulatorType
{
    ACE = 0,
    GDLE = 1
}
