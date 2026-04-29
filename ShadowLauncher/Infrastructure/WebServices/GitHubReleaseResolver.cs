using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ShadowLauncher.Infrastructure.WebServices;

/// <summary>
/// Resolves a GitHub Releases URL to a concrete asset download URL and release tag.
///
/// Supported URL patterns:
///   https://github.com/{owner}/{repo}/releases/latest          - resolves latest release
///   https://github.com/{owner}/{repo}/releases/tag/{tag}/      - resolves that specific tag
///   https://github.com/{owner}/{repo}/releases/download/...    - treated as latest
/// </summary>
public class GitHubReleaseResolver
{
    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShadowLauncher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // Captures owner, repo, and an optional specific tag from /releases/tag/{tag}
    private static readonly Regex GitHubRepoPattern = new(
        @"https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/releases(?:/tag/(?<tag>[^/?#]+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<GitHubReleaseResolver> _logger;

    public GitHubReleaseResolver(ILogger<GitHubReleaseResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>Returns true if the URL points at a GitHub releases page.</summary>
    public static bool IsGitHubReleasesUrl(string? url)
        => !string.IsNullOrWhiteSpace(url) && GitHubRepoPattern.IsMatch(url);

    /// <summary>
    /// Resolves the URL to a release tag and asset download URL via the GitHub API.
    /// When the URL contains a specific tag (e.g. /releases/tag/Daralet) that tag is
    /// used directly. Otherwise the latest release is fetched.
    /// Matches the first .zip asset, or <paramref name="assetName"/> if provided.
    /// Returns null on network failure or if no matching asset is found.
    /// </summary>
    public async Task<GitHubReleaseInfo?> ResolveLatestAsync(string releasesUrl, string? assetName = null)
    {
        var match = GitHubRepoPattern.Match(releasesUrl);
        if (!match.Success) return null;

        var owner       = match.Groups["owner"].Value;
        var repo        = match.Groups["repo"].Value;
        var specificTag = match.Groups["tag"].Success ? match.Groups["tag"].Value : null;

        var apiUrl = specificTag is not null
            ? $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{specificTag}"
            : $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        try
        {
            var json = await _http.GetStringAsync(apiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag  = root.GetProperty("tag_name").GetString() ?? string.Empty;

            if (!root.TryGetProperty("assets", out var assets)) return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var np) ? np.GetString() : null;
                if (name is null) continue;

                var isMatch = assetName is not null
                    ? name.Equals(assetName, StringComparison.OrdinalIgnoreCase)
                    : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                if (!isMatch) continue;

                var downloadUrl = asset.TryGetProperty("browser_download_url", out var du)
                    ? du.GetString() : null;
                if (string.IsNullOrWhiteSpace(downloadUrl)) continue;

                _logger.LogInformation("GitHub release resolved: {Owner}/{Repo} tag={Tag} asset={Asset}",
                    owner, repo, tag, name);
                return new GitHubReleaseInfo(tag, downloadUrl, name);
            }

            _logger.LogWarning("No matching zip asset found in release '{Tag}' of {Owner}/{Repo}",
                tag, owner, repo);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve GitHub release for {Url}", releasesUrl);
            return null;
        }
    }
}

/// <param name="Tag">The release tag, e.g. "Daralet" or "v1.3.0" - used as the version sentinel.</param>
/// <param name="DownloadUrl">Direct download URL for the zip asset.</param>
/// <param name="AssetName">File name of the asset.</param>
public record GitHubReleaseInfo(string Tag, string DownloadUrl, string AssetName);
