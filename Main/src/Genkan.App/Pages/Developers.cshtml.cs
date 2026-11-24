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
        var files = new[] { "EXTEND-SDK.md", "API.md" };
        var blocks = new List<BlogBlock>();

        foreach (var name in files)
        {
            var file = Path.Combine(_paths.Root, "Docs", name);
            if (System.IO.File.Exists(file))
            {
                blocks.AddRange(_blog.Render(System.IO.File.ReadAllText(file)));
            }
        }

        if (blocks.Count == 0)
        {
            Error = "Docs/EXTEND-SDK.md 与 Docs/API.md 都不存在。";
            return;
        }

        Blocks = blocks;
    }
}
