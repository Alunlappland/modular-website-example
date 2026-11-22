using Genkan.Gallery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Genkan.Pages;

[Authorize]
public class GalleryModel : PageModel
{
    private readonly GalleryService _gallery;
    private readonly GalleryPrefsRepository _prefs;

    public GalleryModel(GalleryService gallery, GalleryPrefsRepository prefs)
    {
        _gallery = gallery;
        _prefs = prefs;
    }

    public string Tags { get; private set; } = "safe";

    public string Provider { get; private set; } = "safebooru";

    public string Source { get; private set; } = "safebooru";

    public string View { get; private set; } = "all";

    public string ProviderLabel => Source == "local" ? "本地" : Source;

    public IReadOnlyList<(string Id, string Label)> Sources { get; } = new[]
    {
        ("safebooru", "safebooru"),
        ("konachan", "konachan.net"),
        ("wallhaven", "wallhaven"),
        ("local", "本地（我上传的）"),
    };

    public void OnGet(string? tags, string? source, string? view)
    {
        Provider = _gallery.Provider;
        Source = string.IsNullOrWhiteSpace(source) ? Provider : source.Trim().ToLowerInvariant();
        if (Source is not ("safebooru" or "konachan" or "wallhaven" or "local"))
        {
            Source = Provider;
        }

        View = string.Equals(view, "favorites", StringComparison.OrdinalIgnoreCase) ? "favorites" : "all";
        Tags = string.IsNullOrWhiteSpace(tags) ? _gallery.DefaultTags : tags.Trim();

        // 只记用户主动搜过的标签，避免翻页也写历史
        if (!string.IsNullOrWhiteSpace(tags) && View == "all")
        {
            var owner = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";
            _prefs.RecordHistory(owner, Tags, Source);
        }
    }
}
