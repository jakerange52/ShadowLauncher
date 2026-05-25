using System.Net.Http.Headers;
using System.Text.Json;

namespace ShadowLauncher.Infrastructure.WebServices;

/// <summary>
/// Fetches live player counts from the TreeStats API.
/// Results are cached for <see cref="CacheDuration"/> to avoid hammering the API.
/// Returns an empty dictionary on any failure — population data is supplemental.
/// </summary>
public class TreeStatsService
{
    private const string PlayerCountsUrl = "https://treestats.net/player_counts-latest.json";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private static readonly HttpClient _http = CreateHttpClient();

    private IReadOnlyDictionary<string, PlayerCount> _cache = new Dictionary<string, PlayerCount>(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheTime = DateTime.MinValue;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ShadowLauncher", "1.0"));
        return client;
    }

    /// <summary>
    /// Returns the latest player counts keyed by server name (case-insensitive).
    /// Returns cached results if the cache is still fresh.
    /// Never throws.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, PlayerCount>> GetPlayerCountsAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - _cacheTime < CacheDuration)
            return _cache;

        try
        {
            var json = await _http.GetStringAsync(PlayerCountsUrl, ct);
            var entries = JsonSerializer.Deserialize<PlayerCountEntry[]>(json, JsonOptions);
            if (entries is null) return _cache;

            _cache = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Server))
                .ToDictionary(
                    e => e.Server!,
                    e => new PlayerCount(e.Count, e.Age),
                    StringComparer.OrdinalIgnoreCase);
            _cacheTime = DateTime.UtcNow;
        }
        catch { /* non-fatal — return stale or empty cache */ }

        return _cache;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record PlayerCountEntry(string? Server, int Count, string? Age);
}

public sealed record PlayerCount(int Count, string? Age);
