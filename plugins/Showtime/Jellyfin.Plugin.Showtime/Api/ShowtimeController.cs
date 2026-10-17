using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Showtime.Api;

/// <summary>
/// Endpoints used by the Showtime editor page.
/// </summary>
[ApiController]
[Route("Showtime")]
[Authorize(Policy = Policies.RequiresElevation)]
public class ShowtimeController : ControllerBase
{
    private static readonly string[] BumperCategories =
    {
        "3D Intro", "3D Outro", "Countdown", "Courtesy", "Feature Intro", "Feature Outro", "Intermission",
        "Short Film", "Theater Intro", "Theater Outro", "Trailers Intro", "Trailers Outro", "Trivia Intro", "Trivia Outro",
    };

    private readonly ContentIndex _content;
    private readonly SequenceBuilder _builder;
    private readonly ActionRunner _actions;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;

    public ShowtimeController(ContentIndex content, SequenceBuilder builder, ActionRunner actions, ILibraryManager libraryManager, IUserManager userManager)
    {
        _content = content;
        _builder = builder;
        _actions = actions;
        _libraryManager = libraryManager;
        _userManager = userManager;
    }

    /// <summary>What's in the content library, by section.</summary>
    [HttpGet("Content")]
    public ActionResult<object> GetContent()
    {
        _content.Invalidate();
        var videos = _content.GetVideos();
        var bumperFolders = _content.InFolder(SequenceBuilder.VideoBumpers)
            .Where(v => v.Folders.Length > 1).Select(v => v.Folders[1]);

        return new
        {
            Library = _content.GetLibraryName(),
            Root = _content.GetLibraryRoot(),
            Total = videos.Count,
            Categories = BumperCategories.Concat(bumperFolders).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c).ToArray(),
            Folders = _content.Folders().ToArray(),
            Sections = new Dictionary<string, int>
            {
                [SequenceBuilder.VideoBumpers] = _content.InFolder(SequenceBuilder.VideoBumpers).Count(),
                [SequenceBuilder.RatingsBumpers] = _content.InFolder(SequenceBuilder.RatingsBumpers).Count(),
                [SequenceBuilder.AudioFormatBumpers] = _content.InFolder(SequenceBuilder.AudioFormatBumpers).Count(),
                [SequenceBuilder.TrailersFolder] = _content.InFolder(SequenceBuilder.TrailersFolder).Count(),
                [SequenceBuilder.TriviaReels] = _content.InFolder(SequenceBuilder.TriviaReels).Count(),
            },
        };
    }

    /// <summary>Builds (without tracking) the sequence a user would get for a movie.</summary>
    [HttpGet("Preview")]
    public ActionResult<object> Preview([FromQuery] Guid itemId, [FromQuery] Guid userId)
    {
        var item = _libraryManager.GetItemById(itemId);
        var user = _userManager.GetUserById(userId);
        if (item is null || user is null)
        {
            return NotFound();
        }

        var plan = _builder.Build(item, user);
        var (system, rating) = SequenceBuilder.SplitRating(item.OfficialRating);
        return new
        {
            Feature = item.Name,
            Rating = $"{system} {rating}",
            Audio = SequenceBuilder.DetectAudioFormat(item),
            Sequence = plan?.SequenceName,
            Clips = plan?.Clips.Select(c => new { Step = c.StepIndex + 1, c.Label, c.Item.Name, c.Item.Path, Seconds = (c.Item.RunTimeTicks ?? 0) / TimeSpan.TicksPerSecond, c.Action }).ToArray(),
            plan?.FeatureAction,
            plan?.FeatureEndAction,
            Notes = plan?.Notes ?? new List<string> { "No enabled sequence matches this item and user." },
        };
    }

    /// <summary>Fires an action immediately (ignoring its delay).</summary>
    [HttpPost("Actions/Test")]
    public async Task<ActionResult<object>> TestAction([FromQuery] string name)
    {
        var result = await _actions.RunAsync(name, new ActionContext(null, "test", "Test", "Showtime editor"), skipDelay: true).ConfigureAwait(false);
        return new { Result = result };
    }
}
