using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoesTheDogDie;

/// <summary>
/// Client for the DoesTheDogDie.com API. Results are cached in memory so a
/// title is looked up at most once a week, and failures are cached briefly so
/// a flaky lookup doesn't hammer the API on every playback.
/// </summary>
public class DddClient
{
    private const string BaseUrl = "https://www.doesthedogdie.com";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromHours(6);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DddClient> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public DddClient(IHttpClientFactory httpClientFactory, ILogger<DddClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Looks up warnings for a title. Returns null when the title can't be
    /// matched (or the API is unavailable).
    /// </summary>
    public async Task<WarningsDto?> GetWarningsAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            return null;
        }

        var cacheKey = title.ToLowerInvariant() + "|" + (year?.ToString() ?? string.Empty);
        if (_cache.TryGetValue(cacheKey, out var cached)
            && DateTime.UtcNow - cached.FetchedUtc < (cached.Failed ? FailureTtl : CacheTtl))
        {
            return cached.Result;
        }

        try
        {
            var result = await LookupAsync(title, year, config.ApiKey, config.MinYesVotes, config.MaxTopics, cancellationToken)
                .ConfigureAwait(false);
            StoreCache(cacheKey, new CacheEntry(DateTime.UtcNow, result, false));
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DoesTheDogDie lookup failed for {Title}", title);
            StoreCache(cacheKey, new CacheEntry(DateTime.UtcNow, null, true));
            return null;
        }
    }

    private void StoreCache(string key, CacheEntry entry)
    {
        if (_cache.Count > 5000)
        {
            _cache.Clear();
        }

        _cache[key] = entry;
    }

    private HttpClient CreateClient(string apiKey)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.Add("X-API-KEY", apiKey);
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    }

    private async Task<WarningsDto?> LookupAsync(string title, int? year, string apiKey, int minYesVotes, int maxTopics, CancellationToken cancellationToken)
    {
        using var client = CreateClient(apiKey);

        // 1. Search for the title.
        var searchUrl = $"{BaseUrl}/search?q={Uri.EscapeDataString(title)}";
        using var searchDoc = JsonDocument.Parse(
            await client.GetStringAsync(searchUrl, cancellationToken).ConfigureAwait(false));
        if (!searchDoc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
        {
            return null;
        }

        // 2. Pick the best match: exact name beats partial, matching year
        //    breaks ties, earlier results break remaining ties.
        JsonElement best = default;
        var bestScore = -1;
        var index = 0;
        foreach (var item in items.EnumerateArray())
        {
            var name = GetString(item, "name");
            var itemYear = GetYear(item);
            var score = 0;
            if (string.Equals(name, title, StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (year.HasValue && itemYear.HasValue && Math.Abs(year.Value - itemYear.Value) <= 1)
            {
                score += 2;
            }

            score -= index; // stable preference for earlier (more relevant) results
            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }

            if (++index >= 10)
            {
                break;
            }
        }

        if (best.ValueKind != JsonValueKind.Object || !best.TryGetProperty("id", out var idProp))
        {
            return null;
        }

        var mediaId = idProp.ValueKind == JsonValueKind.Number ? idProp.GetInt64().ToString() : idProp.GetString();
        if (string.IsNullOrEmpty(mediaId))
        {
            return null;
        }

        // 3. Fetch topic stats for the matched media.
        using var mediaDoc = JsonDocument.Parse(
            await client.GetStringAsync($"{BaseUrl}/media/{mediaId}", cancellationToken).ConfigureAwait(false));
        if (!mediaDoc.RootElement.TryGetProperty("topicItemStats", out var stats))
        {
            return null;
        }

        var warnings = new List<WarningDto>();
        foreach (var stat in stats.EnumerateArray())
        {
            var yes = GetInt(stat, "yesSum");
            var no = GetInt(stat, "noSum");
            if (yes <= no || yes < minYesVotes)
            {
                continue;
            }

            var topicName = GetTopicName(stat);
            if (topicName is null)
            {
                continue;
            }

            warnings.Add(new WarningDto
            {
                Name = char.ToUpperInvariant(topicName[0]) + topicName[1..],
                YesVotes = yes,
                NoVotes = no
            });
        }

        return new WarningsDto
        {
            Query = title,
            MatchedName = GetString(best, "name") ?? title,
            MatchedYear = GetYear(best),
            Url = $"{BaseUrl}/media/{mediaId}",
            Warnings = warnings.OrderByDescending(w => w.YesVotes).Take(maxTopics).ToArray()
        };
    }

    private static string? GetTopicName(JsonElement stat)
    {
        if (stat.TryGetProperty("topic", out var topic) && topic.ValueKind == JsonValueKind.Object)
        {
            return GetString(topic, "name") ?? GetString(topic, "smmwDescription") ?? GetString(topic, "doesName");
        }

        return GetString(stat, "topicName");
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return 0;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out var i) ? i : 0,
            JsonValueKind.String => int.TryParse(p.GetString(), out var s) ? s : 0,
            _ => 0
        };
    }

    private static int? GetYear(JsonElement el)
    {
        foreach (var field in new[] { "releaseYear", "year" })
        {
            if (el.TryGetProperty(field, out var p))
            {
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i))
                {
                    return i;
                }

                if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s))
                {
                    return s;
                }
            }
        }

        return null;
    }

    private sealed record CacheEntry(DateTime FetchedUtc, WarningsDto? Result, bool Failed);
}

public class WarningsDto
{
    public string Query { get; set; } = string.Empty;

    public string MatchedName { get; set; } = string.Empty;

    public int? MatchedYear { get; set; }

    public string Url { get; set; } = string.Empty;

    public IReadOnlyList<WarningDto> Warnings { get; set; } = Array.Empty<WarningDto>();
}

public class WarningDto
{
    public string Name { get; set; } = string.Empty;

    public int YesVotes { get; set; }

    public int NoVotes { get; set; }
}
