using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Showtime.Configuration;

/// <summary>
/// What a sequence step does.
/// </summary>
public enum StepType
{
    /// <summary>A random video from a "Video Bumpers/&lt;Category&gt;" folder (or any folder in the content library).</summary>
    Bumper,

    /// <summary>A pre-rendered trivia reel.</summary>
    Trivia,

    /// <summary>N trailers.</summary>
    Trailers,

    /// <summary>The ratings card matching the feature's rating.</summary>
    Rating,

    /// <summary>The audio format bumper matching the feature's best audio track.</summary>
    AudioFormat,

    /// <summary>Marks where the feature starts. Only its action matters; steps after it are ignored.</summary>
    Feature,
}

/// <summary>
/// Where trailers come from.
/// </summary>
public enum TrailerSource
{
    /// <summary>Local trailers belonging to other movies in your libraries.</summary>
    Library,

    /// <summary>Videos in the content library's "Trailers" folder.</summary>
    TrailersFolder,

    /// <summary>Both, mixed.</summary>
    Both,
}

/// <summary>
/// One step of a sequence.
/// </summary>
public class SequenceStep
{
    public StepType Type { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Bumper: CinemaVision category ("Countdown", "Feature Intro", …) or a folder path relative to the content library.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>How many videos (bumpers, trailers, trivia reels).</summary>
    public int Count { get; set; } = 1;

    public TrailerSource TrailerSource { get; set; } = TrailerSource.Library;

    /// <summary>Trailers: prefer movies the viewer hasn't watched.</summary>
    public bool PreferUnwatched { get; set; } = true;

    /// <summary>Trailers: never exceed the feature's parental rating.</summary>
    public bool LimitToFeatureRating { get; set; } = true;

    /// <summary>Name of an <see cref="ActionDefinition"/> fired when this step starts playing.</summary>
    public string Action { get; set; } = string.Empty;
}

/// <summary>
/// A named, ordered list of steps plus the conditions under which it is used.
/// </summary>
public class SequenceDefinition
{
    public string Name { get; set; } = "Default";

    public bool Enabled { get; set; } = true;

    /// <summary>Comma separated. Empty = any genre.</summary>
    public string Genres { get; set; } = string.Empty;

    /// <summary>Comma separated user names. Empty = every user.</summary>
    public string Users { get; set; } = string.Empty;

    /// <summary>Also run before TV episodes.</summary>
    public bool IncludeEpisodes { get; set; }

    /// <summary>Action fired when the feature stops (finished or not) — e.g. raise the lights.</summary>
    public string FeatureEndAction { get; set; } = string.Empty;

    public List<SequenceStep> Steps { get; set; } = new();
}

/// <summary>
/// An HTTP request fired at a point in the sequence (e.g. a Home Assistant webhook).
/// </summary>
public class ActionDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Method { get; set; } = "POST";

    public string Url { get; set; } = string.Empty;

    /// <summary>One "Header: value" per line.</summary>
    public string Headers { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/json";

    /// <summary>Supports {feature}, {user}, {step}, {rating}, {device}.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Wait this long after the step starts before firing.</summary>
    public int DelaySeconds { get; set; }
}

/// <summary>
/// Plugin configuration. Lists must stay empty in the constructor: XmlSerializer appends to pre-filled lists.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Item id of the library holding bumpers, ratings cards, trailers and trivia reels.</summary>
    public string ContentLibraryId { get; set; } = string.Empty;

    /// <summary>Folder of trivia slide images (CinemaVision layout). Empty = "&lt;content library&gt;/Trivia".</summary>
    public string TriviaSourcePath { get; set; } = string.Empty;

    /// <summary>Folder of music for trivia reels. Empty = "&lt;content library&gt;/Music".</summary>
    public string MusicPath { get; set; } = string.Empty;

    /// <summary>Where reels are written; must be inside the content library. Empty = "&lt;content library&gt;/Trivia Reels".</summary>
    public string TriviaReelPath { get; set; } = string.Empty;

    public int TriviaReelCount { get; set; } = 6;

    public int TriviaReelMinutes { get; set; } = 8;

    public int QuestionSeconds { get; set; } = 10;

    public int ClueSeconds { get; set; } = 5;

    public int AnswerSeconds { get; set; } = 6;

    /// <summary>Make extra trivia cards from your own movie library's metadata.</summary>
    public bool GenerateLibraryTrivia { get; set; } = true;

    public List<SequenceDefinition> Sequences { get; set; } = new();

    public List<ActionDefinition> Actions { get; set; } = new();
}
