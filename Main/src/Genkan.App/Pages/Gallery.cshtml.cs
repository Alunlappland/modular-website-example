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

    public void OnGet(string? tags)
    {
        Provider = _gallery.Provider;
        Tags = string.IsNullOrWhiteSpace(tags) ? _gallery.DefaultTags : tags.Trim();
    }
}
