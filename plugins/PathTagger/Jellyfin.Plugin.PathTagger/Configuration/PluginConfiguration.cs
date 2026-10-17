using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PathTagger.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the path-to-tag mappings, one per entry, in the form
    /// <c>&lt;folder path&gt;|&lt;tag&gt;</c>. Every item whose file path is
    /// under the folder gets the tag.
    /// </summary>
    public string[] Mappings { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets a value indicating whether newly added items are tagged
    /// immediately (in addition to the scheduled task).
    /// </summary>
    public bool TagNewItems { get; set; } = true;
}
