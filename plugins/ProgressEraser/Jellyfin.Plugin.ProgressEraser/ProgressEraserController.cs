using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
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

    public ProgressEraserController(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager)
    {
        _userManager = userManager;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
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
        var claim = User.FindFirst(UserIdClaim)?.Value;
        if (!Guid.TryParse(claim, out var userId) || _userManager.GetUserById(userId) is not { } user)
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
            ShowOnDetailPage = config?.ShowOnDetailPage ?? true
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
