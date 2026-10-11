using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Showtime;

/// <summary>
/// A video in the content library, with its path split into folder segments relative to the library root.
/// </summary>
public sealed record ContentVideo(BaseItem Item, string[] Folders, string FileName)
{
    /// <summary>File name without extension, minus CinemaVision's "(3D)" style tags.</summary>
    public string BaseName { get; } = System.Text.RegularExpressions.Regex.Replace(
        System.IO.Path.GetFileNameWithoutExtension(FileName), @"\([^)]*\)", string.Empty).Trim();

    public bool IsUnder(params string[] folders)
    {
        if (Folders.Length < folders.Length)
        {
            return false;
        }

        for (var i = 0; i < folders.Length; i++)
        {
            if (!ContentIndex.SameName(Folders[i], folders[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Cached index of every video in the configured content library, keyed by CinemaVision folder layout.
/// </summary>
public class ContentIndex
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ContentIndex> _logger;
    private readonly object _lock = new();
    private IReadOnlyList<ContentVideo> _videos = Array.Empty<ContentVideo>();
    private DateTime _builtUtc = DateTime.MinValue;
    private string _builtFor = string.Empty;

    public ContentIndex(ILibraryManager libraryManager, ILogger<ContentIndex> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>Case- and punctuation-insensitive comparison ("DTS-X" == "dts x" == "DTSX").</summary>
    public static bool SameName(string a, string b) => Normalize(a) == Normalize(b);

    public static string Normalize(string s)
        => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>The content library's folder, if configured.</summary>
    public Folder? GetLibrary()
    {
        var id = Plugin.Instance?.Configuration.ContentLibraryId;
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var guid))
        {
            return null;
        }

        return _libraryManager.GetItemById(guid) as Folder;
    }

    /// <summary>The media folders of the content library, as configured under Libraries.</summary>
    public string[] GetLibraryPaths()
    {
        var library = GetLibrary();
        if (library is null)
        {
            return Array.Empty<string>();
        }

        var id = library.Id.ToString("N");
        var folder = _libraryManager.GetVirtualFolders().FirstOrDefault(f => string.Equals(f.ItemId, id, StringComparison.OrdinalIgnoreCase));
        return folder?.Locations ?? Array.Empty<string>();
    }

    /// <summary>The first media folder of the content library.</summary>
    public string? GetLibraryRoot() => GetLibraryPaths().FirstOrDefault();

    public void Invalidate()
    {
        lock (_lock)
        {
            _builtUtc = DateTime.MinValue;
        }
    }

    public IReadOnlyList<ContentVideo> GetVideos()
    {
        var libraryId = Plugin.Instance?.Configuration.ContentLibraryId ?? string.Empty;
        lock (_lock)
        {
            if (_builtFor == libraryId && DateTime.UtcNow - _builtUtc < CacheLifetime)
            {
                return _videos;
            }
        }

        var videos = Build();
        lock (_lock)
        {
            _videos = videos;
            _builtUtc = DateTime.UtcNow;
            _builtFor = libraryId;
        }

        return videos;
    }

    /// <summary>Videos in a folder (and its sub-folders) of the content library.</summary>
    public IEnumerable<ContentVideo> InFolder(params string[] folders)
        => GetVideos().Where(v => v.IsUnder(folders));

    /// <summary>Distinct folder paths that contain videos, for the editor's category picker.</summary>
    public IEnumerable<string> Folders()
        => GetVideos().Where(v => v.Folders.Length > 0)
            .Select(v => string.Join('/', v.Folders))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    private List<ContentVideo> Build()
    {
        var library = GetLibrary();
        if (library is null)
        {
            return new List<ContentVideo>();
        }

        var roots = GetLibraryPaths()
            .Select(p => p.TrimEnd('/', '\\') + System.IO.Path.DirectorySeparatorChar)
            .ToArray();

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            AncestorIds = new[] { library.Id },
            Recursive = true,
            IncludeItemTypes = new[] { BaseItemKind.Video, BaseItemKind.Movie, BaseItemKind.MusicVideo, BaseItemKind.Trailer },
            IsVirtualItem = false,
        });

        var result = new List<ContentVideo>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Path))
            {
                continue;
            }

            var root = roots.FirstOrDefault(r => item.Path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
            if (root is null)
            {
                continue;
            }

            var relative = item.Path[root.Length..].Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
            if (relative.Length == 0)
            {
                continue;
            }

            result.Add(new ContentVideo(item, relative[..^1], relative[^1]));
        }

        _logger.LogInformation("Showtime indexed {Count} videos in content library {Library}", result.Count, library.Name);
        return result;
    }
}
