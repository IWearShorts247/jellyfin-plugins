using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Showtime.Trivia;

/// <summary>One frame of a trivia item.</summary>
public sealed record SlideFrame(Func<string, string> Render, int Seconds);

/// <summary>A question/clues/answer group, played together.</summary>
public sealed record SlideSet(string Name, IReadOnlyList<SlideFrame> Frames)
{
    public int Seconds => Frames.Sum(f => f.Seconds);
}

/// <summary>
/// Collects trivia: CinemaVision slide packs from disk, plus cards generated from the movie library.
/// </summary>
public partial class TriviaSource
{
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    private readonly ILibraryManager _libraryManager;

    public TriviaSource(ILibraryManager libraryManager)
    {
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// CinemaVision naming: "Name_q.jpg", "Name_c1.jpg" … "Name_a.jpg" form one question; any other image is a standalone slide.
    /// </summary>
    public List<SlideSet> FromFolder(string root, int question, int clue, int answer)
    {
        var sets = new List<SlideSet>();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            return sets;
        }

        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));

        foreach (var group in files.GroupBy(f => Path.Combine(Path.GetDirectoryName(f)!, SuffixRegex().Replace(Path.GetFileNameWithoutExtension(f), string.Empty))))
        {
            var q = group.FirstOrDefault(f => Suffix(f) == "q");
            var a = group.FirstOrDefault(f => Suffix(f) == "a");
            var clues = group.Where(f => Suffix(f).StartsWith('c')).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

            if (q is null && a is null && clues.Count == 0)
            {
                // Standalone slides (no suffix): each one is its own item.
                sets.AddRange(group.Select(f => new SlideSet(Path.GetFileName(f), new[] { Letterboxed(f, question) })));
                continue;
            }

            var frames = new List<SlideFrame>();
            if (q is not null)
            {
                frames.Add(Letterboxed(q, question));
            }

            frames.AddRange(clues.Select(c => Letterboxed(c, clue)));
            if (a is not null)
            {
                frames.Add(Letterboxed(a, answer));
            }

            sets.Add(new SlideSet(Path.GetFileName(group.Key), frames));
        }

        return sets;
    }

    /// <summary>Questions written from your own library's metadata: taglines, directors, years, co-stars.</summary>
    public List<SlideSet> FromLibrary(string? excludeRoot, int max, int question, int clue, int answer)
    {
        var movies = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie },
            Recursive = true,
            IsVirtualItem = false,
            OrderBy = new[] { (ItemSortBy.Random, SortOrder.Ascending) },
            Limit = max * 3,
        });

        var sets = new List<SlideSet>();
        foreach (var movie in movies)
        {
            if (sets.Count >= max)
            {
                break;
            }

            if (excludeRoot is not null && movie.Path?.StartsWith(excludeRoot, StringComparison.OrdinalIgnoreCase) == true)
            {
                continue;
            }

            var set = Generate(movie, question, clue, answer);
            if (set is not null)
            {
                sets.Add(set);
            }
        }

        return sets;
    }

    private SlideSet? Generate(BaseItem movie, int question, int clue, int answer)
    {
        var people = _libraryManager.GetPeople(movie);
        var director = people.FirstOrDefault(p => p.Type == PersonKind.Director)?.Name;
        var stars = people.Where(p => p.Type == PersonKind.Actor).OrderBy(p => p.SortOrder ?? int.MaxValue).Take(2).Select(p => p.Name).ToList();
        var year = movie.ProductionYear;
        var title = movie.Name;
        var poster = movie.PrimaryImagePath;
        var backdrop = movie.GetImagePath(ImageType.Backdrop, 0);
        var yearText = year?.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var kinds = new List<Func<SlideSet?>>();

        if (!string.IsNullOrWhiteSpace(movie.Tagline) && !movie.Tagline.Contains(title, StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add(() => Build(
                title,
                Card("Name the movie", $"“{movie.Tagline.Trim()}”", "Which film used this tagline?", null, question),
                yearText is null ? null : Card("Clue", $"Released in {yearText}", director is null ? null : $"Directed by {director}", null, clue),
                Answer(title, Join(yearText, director), poster, backdrop, answer)));
        }

        if (director is not null && yearText is not null)
        {
            kinds.Add(() => Build(
                title,
                Card("Behind the camera", $"Who directed {title}?", $"({yearText})", backdrop, question),
                null,
                Answer(director, $"{title} · {yearText}", poster, backdrop, answer)));
        }

        if (stars.Count == 2 && yearText is not null)
        {
            kinds.Add(() => Build(
                title,
                Card("Name the movie", $"{stars[0]} and {stars[1]} starred together in which {yearText} film?", null, null, question),
                director is null ? null : Card("Clue", $"Directed by {director}", null, null, clue),
                Answer(title, Join(yearText, director), poster, backdrop, answer)));
        }

        if (yearText is not null)
        {
            kinds.Add(() => Build(
                title,
                Card("Release year", $"In what year was {title} released?", stars.Count > 0 ? $"Starring {string.Join(" and ", stars)}" : null, backdrop, question),
                null,
                Answer(yearText, title, poster, backdrop, answer)));
        }

        return kinds.Count == 0 ? null : kinds[Random.Shared.Next(kinds.Count)]();
    }

    private static SlideSet Build(string name, SlideFrame q, SlideFrame? c, SlideFrame a)
        => new(name, c is null ? new[] { q, a } : new[] { q, c, a });

    private static SlideFrame Card(string header, string text, string? sub, string? backdrop, int seconds)
        => new(dir => Output(dir, path => SlideCanvas.TextCard(path, header, text, sub, backdrop)), seconds);

    private static SlideFrame Answer(string answer, string? detail, string? poster, string? backdrop, int seconds)
        => new(dir => Output(dir, path => SlideCanvas.AnswerCard(path, answer, detail, poster, backdrop)), seconds);

    private static SlideFrame Letterboxed(string source, int seconds)
        => new(dir => Output(dir, path => SlideCanvas.Letterbox(source, path)), seconds);

    private static string Output(string dir, Action<string> draw)
    {
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jpg");
        draw(path);
        return path;
    }

    private static string? Join(string? a, string? b)
        => a is null ? b : b is null ? a : $"{a} · {b}";

    private static string Suffix(string file)
    {
        var m = SuffixRegex().Match(Path.GetFileNameWithoutExtension(file));
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : string.Empty;
    }

    [GeneratedRegex(@"_(q|a|c\d*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SuffixRegex();
}
