using Genkan.Blog;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

public class DevelopersModel : PageModel
{
    private readonly BlogService _blog;
    private readonly GenkanPaths _paths;

    public DevelopersModel(BlogService blog, GenkanPaths paths)
    {
        _blog = blog;
        _paths = paths;
    }

    public IReadOnlyList<BlogBlock> Blocks { get; private set; } = Array.Empty<BlogBlock>();

    public string? Error { get; private set; }

    public void OnGet()
    {
        var file = Path.Combine(_paths.Root, "Docs", "EXTEND-SDK.md");
        if (!System.IO.File.Exists(file))
        {
            Error = "找不到 Docs/EXTEND-SDK.md。";
            return;
        }

        Blocks = _blog.Render(System.IO.File.ReadAllText(file));
    }
}
