using Genkan.Gallery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class GalleryModel : PageModel
{
    private readonly GalleryService _gallery;

    public GalleryModel(GalleryService gallery) => _gallery = gallery;

    public string Tags { get; private set; } = "safe";

    public string Provider { get; private set; } = "konachan";

    public string ProviderLabel => Provider == "safebooru" ? "safebooru" : "konachan.net";

    public string Source { get; private set; } = "safebooru";

    public IReadOnlyList<(string Id, string Label)> Sources { get; } = new[]
    {
        ("safebooru", "safebooru"),
        ("konachan", "konachan.net"),
        ("local", "本地（我上传的）"),
    };

    public void OnGet(string? tags, string? source)
    {
        Provider = _gallery.Provider;
        Source = string.IsNullOrWhiteSpace(source) ? Provider : source.Trim().ToLowerInvariant();
        if (Source is not ("safebooru" or "konachan" or "local"))
        {
            Source = Provider;
        }

        Tags = string.IsNullOrWhiteSpace(tags) ? _gallery.DefaultTags : tags.Trim();
    }
}
