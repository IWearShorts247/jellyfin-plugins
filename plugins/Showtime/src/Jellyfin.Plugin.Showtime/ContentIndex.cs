using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
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

    /// <summary>CinemaVision marks 3D variants with a tag like "(3D)" or "(3DSBS)" in the file name.</summary>
    public bool Is3D { get; } = System.Text.RegularExpressions.Regex.IsMatch(FileName, @"\([^)]*3D[^)]*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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

    /// <summary>
    /// The configured content library as Jellyfin lists it under Libraries. Matched by name first, because 12.x
    /// leaves <c>ItemId</c> empty whenever it can't pair a library with its database folder (e.g. before a first scan).
    /// </summary>
    public VirtualFolderInfo? GetVirtualFolder()
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return null;
        }

        var folders = _libraryManager.GetVirtualFolders();
        if (!string.IsNullOrWhiteSpace(config.ContentLibraryName))
        {
            var byName = folders.FirstOrDefault(f => string.Equals(f.Name, config.ContentLibraryName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }
        }

        // Configs saved by 0.1/0.2 only have the id.
        if (Guid.TryParse(config.ContentLibraryId, out var guid))
        {
            var id = guid.ToString("N");
            return folders.FirstOrDefault(f => string.Equals(f.ItemId, id, StringComparison.OrdinalIgnoreCase))
                ?? folders.FirstOrDefault(f => string.Equals(f.Name, _libraryManager.GetItemById(guid)?.Name, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    /// <summary>The library's database folder, if Jellyfin has one for it.</summary>
    public Folder? GetLibrary()
    {
        var folder = GetVirtualFolder();
        if (folder is null)
        {
            return null;
        }

        if (Guid.TryParse(folder.ItemId, out var id) && _libraryManager.GetItemById(id) is Folder byId)
        {
            return byId;
        }

        var children = _libraryManager.GetUserRootFolder().Children.OfType<Folder>().ToList();
        return children.FirstOrDefault(c => string.Equals(System.IO.Path.GetFileName(c.Path), folder.Name, StringComparison.OrdinalIgnoreCase))
            ?? children.FirstOrDefault(c => string.Equals(c.Name, folder.Name, StringComparison.OrdinalIgnoreCase));
    }

    public string? GetLibraryName() => GetVirtualFolder()?.Name;

    /// <summary>The media folders of the content library, as configured under Libraries.</summary>
    public string[] GetLibraryPaths() => GetVirtualFolder()?.Locations ?? Array.Empty<string>();

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
        var libraryId = (Plugin.Instance?.Configuration.ContentLibraryName ?? string.Empty) + "|" + (Plugin.Instance?.Configuration.ContentLibraryId ?? string.Empty);
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
        var roots = GetLibraryPaths()
            .Select(p => p.TrimEnd('/', '\\') + System.IO.Path.DirectorySeparatorChar)
            .ToArray();
        if (roots.Length == 0)
        {
            return new List<ContentVideo>();
        }

        var query = new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = new[] { BaseItemKind.Video, BaseItemKind.Movie, BaseItemKind.MusicVideo, BaseItemKind.Trailer },
            IsVirtualItem = false,
        };

        var library = GetLibrary();
        IReadOnlyList<BaseItem> items = Array.Empty<BaseItem>();
        if (library is not null)
        {
            query.AncestorIds = new[] { library.Id };
            items = _libraryManager.GetItemList(query);
        }

        if (items.Count == 0)
        {
            // No usable library folder: fall back to every video on the server, filtered by path below.
            query.AncestorIds = Array.Empty<Guid>();
            items = _libraryManager.GetItemList(query);
        }

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

            var relative = item.Path[root.Length..].Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (relative.Length == 0)
            {
                continue;
            }

            result.Add(new ContentVideo(item, relative[..^1], relative[^1]));
        }

        _logger.LogInformation("Showtime indexed {Count} videos in content library {Library} ({Roots})", result.Count, GetLibraryName(), string.Join(", ", roots));
        return result;
    }
}
