namespace ShadowLauncher.Core.Models;

/// <summary>
/// Describes a DAT file that belongs to a <see cref="DatSet"/>.
/// Used to filter which filenames are extracted from a zip archive.
/// </summary>
public class DatFile
{
    /// <summary>
    /// The exact filename as acclient.exe expects it in its working directory,
    /// e.g. "client_portal.dat".
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}
