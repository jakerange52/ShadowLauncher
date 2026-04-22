namespace ShadowLauncher.Core.Models;

/// <summary>
/// Describes a single DAT file that belongs to a DatSet.
/// </summary>
public class DatFile
{
    /// <summary>
    /// The exact filename as acclient.exe expects it in its working directory.
    /// e.g. "client_portal.dat"
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Optional URL to download this DAT file from if it is not already cached locally.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional SHA-256 hex checksum used to verify the downloaded file's integrity.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
}
