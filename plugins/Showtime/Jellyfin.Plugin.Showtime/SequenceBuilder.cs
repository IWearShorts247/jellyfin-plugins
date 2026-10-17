using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Showtime.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Showtime;

/// <summary>One clip in a built sequence.</summary>
public sealed record PlannedClip(BaseItem Item, int StepIndex, StepType StepType, string Label, string Action);

/// <summary>A sequence resolved for one feature and one viewer.</summary>
public sealed class SequencePlan
{
    public required BaseItem Feature { get; init; }

    public required User User { get; init; }

    public required string SequenceName { get; init; }

    public List<PlannedClip> Clips { get; } = new();

    public string FeatureAction { get; set; } = string.Empty;

    public string FeatureEndAction { get; set; } = string.Empty;

    /// <summary>Human-readable notes about skipped steps (shown in the editor's preview).</summary>
    public List<string> Notes { get; } = new();

    public string? AudioFormat { get; set; }
}

/// <summary>
/// Turns a feature + viewer into a concrete list of intro videos.
/// </summary>
public class SequenceBuilder
{
    public const string VideoBumpers = "Video Bumpers";
    public const string RatingsBumpers = "Ratings Bumpers";
    public const string AudioFormatBumpers = "Audio Format Bumpers";
    public const string TrailersFolder = "Trailers";
    public const string TriviaReels = "Trivia Reels";

    /// <summary>Audio format → CinemaVision folder names to try, best match first.</summary>
    private static readonly Dictionary<string, string[]> AudioFallbacks = new()
    {
        ["Dolby Atmos"] = new[] { "Dolby Atmos", "Dolby TrueHD", "Dolby Digital Plus", "Dolby Digital", "Other" },
        ["Dolby TrueHD"] = new[] { "Dolby TrueHD", "Dolby Digital", "Other" },
        ["Dolby Digital Plus"] = new[] { "Dolby Digital Plus", "Dolby Digital", "Other" },
        ["Dolby Digital"] = new[] { "Dolby Digital", "Other" },
        ["DTS-X"] = new[] { "DTS-X", "DTS-HD Master Audio", "DTS", "Other" },
        ["DTS-HD Master Audio"] = new[] { "DTS-HD Master Audio", "DTS", "Other" },
        ["DTS"] = new[] { "DTS", "Other" },
        ["Auro-3D"] = new[] { "Auro-3D", "Other" },
        ["Other"] = new[] { "Other" },
    };

    /// <summary>Two-letter rating prefixes Jellyfin uses → CinemaVision rating system folder.</summary>
    private static readonly Dictionary<string, string> RatingSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = "MPAA",
        ["GB"] = "BBFC",
        ["UK"] = "BBFC",
        ["DE"] = "FSK",
        ["BR"] = "DEJUS",
    };

    private readonly ContentIndex _content;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userData;
    private readonly ILogger<SequenceBuilder> _logger;

    public SequenceBuilder(ContentIndex content, ILibraryManager libraryManager, IUserDataManager userData, ILogger<SequenceBuilder> logger)
    {
        _content = content;
        _libraryManager = libraryManager;
        _userData = userData;
        _logger = logger;
    }

    /// <summary>The classic CinemaVision running order, used when no sequence is configured.</summary>
    public static SequenceDefinition DefaultSequence() => new()
    {
        Name = "Classic",
        Steps =
        {
            new SequenceStep { Type = StepType.Bumper, Category = "Trivia Intro" },
            new SequenceStep { Type = StepType.Trivia },
            new SequenceStep { Type = StepType.Bumper, Category = "Trivia Outro" },
            new SequenceStep { Type = StepType.Bumper, Category = "Trailers Intro" },
            new SequenceStep { Type = StepType.Trailers, Count = 2 },
            new SequenceStep { Type = StepType.Bumper, Category = "Trailers Outro" },
            new SequenceStep { Type = StepType.Bumper, Category = "Feature Intro" },
            new SequenceStep { Type = StepType.Bumper, Category = "Countdown" },
            new SequenceStep { Type = StepType.Rating },
            new SequenceStep { Type = StepType.AudioFormat },
            new SequenceStep { Type = StepType.Feature },
        },
    };

    /// <summary>Picks the sequence that applies to this feature and viewer, or null for none.</summary>
    public SequenceDefinition? SelectSequence(BaseItem feature, User user)
    {
        var isMovie = feature is Movie;
        var isEpisode = feature is Episode;
        if (!isMovie && !isEpisode)
        {
            return null;
        }

        var config = Plugin.Instance!.Configuration;
        var sequences = config.Sequences.Count > 0 ? config.Sequences : new List<SequenceDefinition> { DefaultSequence() };

        // Sequences with conditions win over catch-alls, so list order only matters among equals.
        return sequences
            .Where(s => s.Enabled && (isMovie || s.IncludeEpisodes))
            .Where(s => MatchesList(s.Users, new[] { user.Username }))
            .Where(s => MatchesList(s.Genres, GenresOf(feature)))
            .OrderByDescending(s => (string.IsNullOrWhiteSpace(s.Genres) ? 0 : 2) + (string.IsNullOrWhiteSpace(s.Users) ? 0 : 1))
            .FirstOrDefault();
    }

    public SequencePlan? Build(BaseItem feature, User user)
    {
        var sequence = SelectSequence(feature, user);
        if (sequence is null)
        {
            return null;
        }

        var plan = new SequencePlan { Feature = feature, User = user, SequenceName = sequence.Name, FeatureEndAction = sequence.FeatureEndAction };
        var used = new HashSet<Guid>();

        for (var i = 0; i < sequence.Steps.Count; i++)
        {
            var step = sequence.Steps[i];
            if (!step.Enabled)
            {
                continue;
            }

            if (step.Type == StepType.Feature)
            {
                plan.FeatureAction = step.Action;
                break;
            }

            var label = Describe(step);
            var want3D = feature is Video { Is3D: true };
            var picked = step.Type switch
            {
                StepType.Bumper => Pick(Fit3D(_content.InFolder(VideoBumpers, step.Category).Concat(_content.InFolder(step.Category.Split(new[] { '/', '\\' }))), want3D).Select(v => v.Item), step.Count, user, used),
                StepType.Trivia => Pick(_content.InFolder(TriviaReels).Select(v => v.Item), step.Count, user, used),
                StepType.Trailers => PickTrailers(step, feature, user, used),
                StepType.Rating => PickRating(feature, plan, want3D),
                StepType.AudioFormat => PickAudio(feature, plan, want3D),
                _ => new List<BaseItem>(),
            };

            if (picked.Count == 0)
            {
                plan.Notes.Add($"Step {i + 1} ({label}): nothing to play, skipped.");
                continue;
            }

            foreach (var item in picked)
            {
                used.Add(item.Id);
                plan.Clips.Add(new PlannedClip(item, i, step.Type, label, step.Action));
            }
        }

        return plan;
    }

    public static string Describe(SequenceStep step) => step.Type switch
    {
        StepType.Bumper => $"Bumper: {step.Category}",
        StepType.Trailers => $"{step.Count} trailer(s)",
        StepType.Trivia => "Trivia",
        StepType.Rating => "Rating card",
        StepType.AudioFormat => "Audio format",
        StepType.Feature => "Feature",
        _ => step.Type.ToString(),
    };

    /// <summary>Best-guess CinemaVision audio format name for the feature's main audio track.</summary>
    public static string? DetectAudioFormat(BaseItem feature)
    {
        var audio = feature.GetMediaStreams().Where(s => s.Type == MediaStreamType.Audio).ToList();
        var main = audio.FirstOrDefault(s => s.IsDefault) ?? audio.FirstOrDefault();
        if (main is null)
        {
            return null;
        }

        var codec = (main.Codec ?? string.Empty).ToLowerInvariant();
        var text = string.Join(' ', main.Profile, main.Title, main.DisplayTitle).ToLowerInvariant();

        if (text.Contains("atmos"))
        {
            return "Dolby Atmos";
        }

        if (text.Contains("dts:x") || text.Contains("dts-x") || text.Contains("dts x"))
        {
            return "DTS-X";
        }

        if (text.Contains("auro"))
        {
            return "Auro-3D";
        }

        return codec switch
        {
            "truehd" or "mlp" => "Dolby TrueHD",
            "eac3" => "Dolby Digital Plus",
            "ac3" => "Dolby Digital",
            "dts" when text.Contains("ma") || text.Contains("hd") => "DTS-HD Master Audio",
            "dts" => "DTS",
            _ => "Other",
        };
    }

    /// <summary>Splits a Jellyfin rating like "PG-13", "GB-15" or "DE-12" into (system folder, rating name).</summary>
    public static (string System, string Rating) SplitRating(string? officialRating)
    {
        var rating = string.IsNullOrWhiteSpace(officialRating) ? "NR" : officialRating.Trim();
        var dash = rating.IndexOf('-');
        if (dash == 2 && RatingSystems.TryGetValue(rating[..2], out var system))
        {
            return (system, rating[3..]);
        }

        return ("MPAA", rating);
    }

    private static bool MatchesList(string configured, IEnumerable<string> values)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return true;
        }

        var wanted = configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return values.Any(v => wanted.Any(w => ContentIndex.SameName(v, w)));
    }

    private static IEnumerable<string> GenresOf(BaseItem feature)
        => feature is Episode ep && ep.Series is not null ? ep.Series.Genres : feature.Genres;

    /// <summary>
    /// Random pick that favours what this viewer has seen least recently, so bumpers rotate instead of repeating.
    /// </summary>
    private List<BaseItem> Pick(IEnumerable<BaseItem> candidates, int count, User user, HashSet<Guid> used)
    {
        var pool = candidates.Where(c => !used.Contains(c.Id)).DistinctBy(c => c.Id).ToList();
        if (pool.Count == 0 || count <= 0)
        {
            return new List<BaseItem>();
        }

        var ranked = pool
            .Select(item => (item, last: _userData.GetUserData(user, item)?.LastPlayedDate ?? DateTime.MinValue, roll: Random.Shared.Next()))
            .OrderBy(x => x.last)
            .ThenBy(x => x.roll)
            .ToList();

        // Choose randomly from the least-recently-seen half (at least `count`).
        var window = Math.Min(ranked.Count, Math.Max(count, (ranked.Count + 1) / 2));
        return ranked.Take(window).OrderBy(_ => Random.Shared.Next()).Take(count).Select(x => x.item).ToList();
    }

    /// <summary>
    /// Keeps 3D-tagged clips for 3D features and plain clips for everything else. A 3D feature falls back to
    /// plain clips when no 3D version exists.
    /// </summary>
    private static List<ContentVideo> Fit3D(IEnumerable<ContentVideo> candidates, bool want3D)
    {
        var list = candidates.ToList();
        if (want3D && list.Any(v => v.Is3D))
        {
            return list.Where(v => v.Is3D).ToList();
        }

        return list.Where(v => !v.Is3D).ToList();
    }

    private List<BaseItem> PickRating(BaseItem feature, SequencePlan plan, bool want3D)
    {
        var rated = feature is Episode ep && string.IsNullOrEmpty(ep.OfficialRating) ? ep.Series : feature;
        var (system, rating) = SplitRating(rated?.OfficialRating);
        var all = _content.InFolder(RatingsBumpers).ToList();

        var match = Fit3D(all.Where(v => v.IsUnder(RatingsBumpers, system) && ContentIndex.SameName(v.BaseName, rating)), want3D);
        if (match.Count == 0)
        {
            match = Fit3D(all.Where(v => ContentIndex.SameName(v.BaseName, rating)), want3D);
        }

        if (match.Count == 0)
        {
            plan.Notes.Add($"No ratings card named \"{rating}\" under {RatingsBumpers}/{system}.");
        }

        return match.Count == 0 ? new List<BaseItem>() : new List<BaseItem> { match[Random.Shared.Next(match.Count)].Item };
    }

    private List<BaseItem> PickAudio(BaseItem feature, SequencePlan plan, bool want3D)
    {
        var format = DetectAudioFormat(feature);
        plan.AudioFormat = format;
        if (format is null)
        {
            return new List<BaseItem>();
        }

        foreach (var folder in AudioFallbacks[format])
        {
            var matches = Fit3D(_content.InFolder(AudioFormatBumpers, folder), want3D);
            if (matches.Count > 0)
            {
                if (!ContentIndex.SameName(folder, format))
                {
                    plan.Notes.Add($"Audio is {format}; no bumper for it, used {folder}.");
                }

                return new List<BaseItem> { matches[Random.Shared.Next(matches.Count)].Item };
            }
        }

        return new List<BaseItem>();
    }

    private List<BaseItem> PickTrailers(SequenceStep step, BaseItem feature, User user, HashSet<Guid> used)
    {
        var count = Math.Max(0, step.Count);
        var fromFolder = step.TrailerSource is TrailerSource.TrailersFolder or TrailerSource.Both
            ? Pick(_content.InFolder(TrailersFolder).Select(v => v.Item), count, user, used)
            : new List<BaseItem>();

        var fromLibrary = step.TrailerSource is TrailerSource.Library or TrailerSource.Both
            ? LibraryTrailers(step, feature, user, used, count)
            : new List<BaseItem>();

        var all = fromFolder.Concat(fromLibrary).OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        return all;
    }

    /// <summary>Local trailers of other movies the viewer can see — "coming attractions" from your own shelf.</summary>
    private List<BaseItem> LibraryTrailers(SequenceStep step, BaseItem feature, User user, HashSet<Guid> used, int count)
    {
        var contentRoot = _content.GetLibraryRoot();
        var result = new List<BaseItem>();

        // First pass honours "prefer unwatched"; second pass tops up from everything.
        var passes = step.PreferUnwatched ? new bool?[] { false, null } : new bool?[] { null };
        foreach (var played in passes)
        {
            if (result.Count >= count)
            {
                break;
            }

            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                IsVirtualItem = false,
                IsPlayed = played,
                ExcludeItemIds = new[] { feature.Id },
                OrderBy = new[] { (ItemSortBy.Random, SortOrder.Ascending) },
                Limit = 60,
            };

            if (step.LimitToFeatureRating && !string.IsNullOrEmpty(feature.OfficialRating))
            {
                query.MaxParentalRating = feature.GetParentalRatingScore();
            }

            foreach (var movie in _libraryManager.GetItemList(query))
            {
                if (result.Count >= count)
                {
                    break;
                }

                if (contentRoot is not null && movie.Path?.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase) == true)
                {
                    continue;
                }

                var trailer = movie.GetExtras(new[] { ExtraType.Trailer }).FirstOrDefault(t => !used.Contains(t.Id) && result.All(r => r.Id != t.Id));
                if (trailer is not null)
                {
                    result.Add(trailer);
                }
            }
        }

        return result;
    }
}
