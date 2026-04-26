using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ShadowLauncher.Infrastructure.Updates;

/// <summary>
/// Checks for a newer version of ShadowLauncher by querying the GitHub Releases API,
/// and can download the new installer to a temp path ready to be launched.
/// </summary>
public class UpdateChecker
{
    // Update these to match your actual GitHub repo.
    private const string GitHubOwner = "jakerange52";
    private const string GitHubRepo  = "ShadowLauncher";
    private const string ApiUrl      = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    /// <summary>Returns the version of the currently running assembly.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

    /// <summary>
    /// Queries the GitHub Releases API for the latest release and compares it to
    /// the running version. Never throws — returns a faulted result on any error.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync()
    {
        try
        {
            using var http = MakeHttpClient();
            var json = await http.GetStringAsync(ApiUrl);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // GitHub tag names are typically "v0.2.0" or "0.2.0"
            var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var versionStr = tagName.TrimStart('v');

            if (!Version.TryParse(versionStr, out var remote))
                return UpdateCheckResult.Faulted($"Could not parse remote version: '{tagName}'");

            var notes = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;

            // Find the ShadowLauncher-Setup.exe asset in the release.
            var downloadUrl = string.Empty;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var np) ? np.GetString() : null;
                    if (name is not null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var up)
                            ? up.GetString() ?? string.Empty
                            : string.Empty;
                        break;
                    }
                }
            }

            var current = CurrentVersion;
            return new UpdateCheckResult
            {
                Success        = true,
                CurrentVersion = current,
                RemoteVersion  = remote,
                UpdateAvailable = remote > current,
                DownloadUrl    = downloadUrl,
                ReleaseNotes   = notes,
            };
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Faulted($"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Faulted($"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads the installer from <paramref name="url"/> to a temp file,
    /// reporting progress via <paramref name="progress"/> (0–100).
    /// Returns the path to the downloaded file.
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        string url,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destPath = Path.Combine(Path.GetTempPath(), "ShadowLauncher-Setup-Update.exe");

        using var http = MakeHttpClient();
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        long received = 0;

        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buffer = new byte[81920];
        int read;
        while ((read = await src.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (totalBytes > 0)
                progress?.Report((int)Math.Clamp(received * 100.0 / totalBytes, 0, 100));
        }

        return destPath;
    }

    private static HttpClient MakeHttpClient()
    {
        // Note: short-lived usage only — each call site wraps this in a using block.
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub API requires a User-Agent header.
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ShadowLauncher", CurrentVersion.ToString()));
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}

/// <summary>Result returned by <see cref="UpdateChecker.CheckAsync"/>.</summary>
public class UpdateCheckResult
{
    public bool Success { get; init; }
    public bool UpdateAvailable { get; init; }
    public Version? CurrentVersion { get; init; }
    public Version? RemoteVersion { get; init; }
    public string DownloadUrl { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;

    public static UpdateCheckResult Faulted(string message) =>
        new() { Success = false, ErrorMessage = message };
}