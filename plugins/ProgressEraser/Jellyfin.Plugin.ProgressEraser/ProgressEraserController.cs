using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using Jellyfin.Plugin.ProgressEraser.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ProgressEraser;

[ApiController]
[Route("ProgressEraser")]
public class ProgressEraserController : ControllerBase
{
    // Jellyfin's InternalClaimTypes.UserId; Jellyfin.Api is not available to plugins.
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly IUserManager _userManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly NextUpHider _hider;

    public ProgressEraserController(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager, NextUpHider hider)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _hider = hider;
    }

    private User? CurrentUser()
    {
        var claim = User.FindFirst(UserIdClaim)?.Value;
        return Guid.TryParse(claim, out var userId) ? _userManager.GetUserById(userId) : null;
    }

    /// <summary>
    /// Clears the calling user's playback position on an item. Played state and
    /// play count are left alone. For a folder (series, season, box set) every
    /// in-progress video inside it is reset.
    /// </summary>
    [HttpPost("{itemId}")]
    [Authorize]
    public ActionResult<ResetResultDto> Reset([FromRoute] Guid itemId, CancellationToken cancellationToken)
    {
        if (CurrentUser() is not { } user)
        {
            return Unauthorized();
        }

        var item = _libraryManager.GetItemById<BaseItem>(itemId, user);
        if (item is null)
        {
            return NotFound();
        }

        IReadOnlyList<BaseItem> targets = item is Folder
            ? _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                ParentId = item.Id,
                Recursive = true,
                IsResumable = true,
                MediaTypes = [MediaType.Video]
            })
            : [item];

        var reset = 0;
        foreach (var target in targets)
        {
            var data = _userDataManager.GetUserData(user, target);
            if (data is null || data.PlaybackPositionTicks == 0)
            {
                continue;
            }

            data.PlaybackPositionTicks = 0;
            _userDataManager.SaveUserData(user, target, data, UserDataSaveReason.UpdateUserData, cancellationToken);
            reset++;
        }

        return new ResetResultDto { ItemsReset = reset };
    }

    // ---- Next Up ----

    /// <summary>Shows the calling user has hidden from Next Up.</summary>
    [HttpGet("NextUp/Hidden")]
    [Authorize]
    public ActionResult<IEnumerable<HiddenSeriesDto>> GetHidden()
    {
        if (CurrentUser() is not { } user)
        {
            return Unauthorized();
        }

        return Ok(_hider.For(user.Id).Select(ToDto));
    }

    /// <summary>Every user's hidden shows, for the settings page.</summary>
    [HttpGet("NextUp/Hidden/All")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult<IEnumerable<HiddenSeriesDto>> GetAllHidden()
    {
        return Ok(_hider.All().Select(ToDto));
    }

    /// <summary>Whether the show an item belongs to is hidden for the calling user.</summary>
    [HttpGet("NextUp/Status/{itemId}")]
    [Authorize]
    public ActionResult<NextUpStatusDto> GetStatus([FromRoute] Guid itemId)
    {
        if (CurrentUser() is not { } user)
        {
            return Unauthorized();
        }

        var series = ResolveSeries(itemId, user);
        if (series is null)
        {
            return NotFound();
        }

        return new NextUpStatusDto
        {
            SeriesId = series.Id,
            SeriesName = series.Name,
            Hidden = _hider.IsHidden(user.Id, series.Id)
        };
    }

    /// <summary>Hides the show an episode, season or series belongs to from the calling user's Next Up.</summary>
    [HttpPost("NextUp/Hide/{itemId}")]
    [Authorize]
    public ActionResult<HiddenSeriesDto> Hide([FromRoute] Guid itemId)
    {
        if (CurrentUser() is not { } user)
        {
            return Unauthorized();
        }

        var series = ResolveSeries(itemId, user);
        if (series is null)
        {
            return NotFound();
        }

        return ToDto(_hider.Hide(user.Id, series));
    }

    /// <summary>Puts a show back into the calling user's Next Up.</summary>
    [HttpDelete("NextUp/Hide/{seriesId}")]
    [Authorize]
    public ActionResult Unhide([FromRoute] Guid seriesId)
    {
        if (CurrentUser() is not { } user)
        {
            return Unauthorized();
        }

        return _hider.Unhide(user.Id, seriesId) ? NoContent() : NotFound();
    }

    /// <summary>Puts a show back into another user's Next Up, from the settings page.</summary>
    [HttpDelete("NextUp/Hidden/{userId}/{seriesId}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public ActionResult UnhideForUser([FromRoute] Guid userId, [FromRoute] Guid seriesId)
    {
        return _hider.Unhide(userId, seriesId) ? NoContent() : NotFound();
    }

    private Series? ResolveSeries(Guid itemId, User user)
    {
        var item = _libraryManager.GetItemById<BaseItem>(itemId, user);
        return item is null ? null : _hider.ResolveSeries(item);
    }

    private HiddenSeriesDto ToDto(HiddenSeries entry)
    {
        return new HiddenSeriesDto
        {
            UserId = entry.UserId,
            UserName = _userManager.GetUserById(entry.UserId)?.Username ?? "(deleted user)",
            SeriesId = entry.SeriesId,
            SeriesName = entry.SeriesName,
            HiddenAt = entry.HiddenAt
        };
    }

    /// <summary>
    /// Serves the client script. Anonymous because index.html loads it before
    /// the user signs in.
    /// </summary>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    public ActionResult GetClientScript()
    {
        using var stream = typeof(ProgressEraserController).Assembly
            .GetManifestResourceStream("Jellyfin.Plugin.ProgressEraser.Web.progresseraser.js");
        if (stream is null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream);
        var js = reader.ReadToEnd();
        var config = Plugin.Instance?.Configuration;
        var configJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            ShowOnAllCards = config?.ShowOnAllCards ?? false,
            ShowOnDetailPage = config?.ShowOnDetailPage ?? true,
            NextUpButton = config?.NextUpButton ?? true
        });
        js = js.Replace("__PROGRESSERASER_CONFIG__", configJson, StringComparison.Ordinal);

        Response.Headers.CacheControl = "no-cache";
        return Content(js, "application/javascript");
    }
}

public class ResetResultDto
{
    public int ItemsReset { get; set; }
}

public class HiddenSeriesDto
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public Guid SeriesId { get; set; }

    public string SeriesName { get; set; } = string.Empty;

    public DateTime HiddenAt { get; set; }
}

public class NextUpStatusDto
{
    public Guid SeriesId { get; set; }

    public string SeriesName { get; set; } = string.Empty;

    public bool Hidden { get; set; }
}
