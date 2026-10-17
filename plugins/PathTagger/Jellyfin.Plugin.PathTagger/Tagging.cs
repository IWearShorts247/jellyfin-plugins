using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.PathTagger;

/// <summary>Shared mapping/tagging logic used by the task and the live handler.</summary>
public static class Tagging
{
    /// <summary>
    /// Parses configured mappings ("folder|tag" per entry) into
    /// (prefix, tag) pairs. Prefixes are normalized to end with a separator so
    /// "D:\Movies\His" doesn't also match "D:\Movies\Hers2".
    /// </summary>
    public static List<(string Prefix, string Tag)> ParseMappings()
    {
        var result = new List<(string, string)>();
        foreach (var line in Plugin.Instance?.Configuration.Mappings ?? Array.Empty<string>())
        {
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                continue;
            }

            var prefix = parts[0].TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
            result.Add((prefix, parts[1]));
        }

        return result;
    }

    /// <summary>
    /// Returns the tags that apply to the item's path but are missing from it.
    /// </summary>
    public static List<string> GetMissingTags(BaseItem item, List<(string Prefix, string Tag)> mappings)
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(item.Path))
        {
            return missing;
        }

        foreach (var (prefix, tag) in mappings)
        {
            if (item.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(tag);
            }
        }

        return missing;
    }
}
