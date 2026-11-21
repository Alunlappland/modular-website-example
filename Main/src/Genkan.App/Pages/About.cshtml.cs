using Genkan.Blog;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

public class AboutModel : PageModel
{
    private readonly BlogService _blog;
    private readonly GenkanPaths _paths;

    public AboutModel(BlogService blog, GenkanPaths paths)
    {
        _blog = blog;
        _paths = paths;
    }

    public IReadOnlyList<BlogBlock> Blocks { get; private set; } = Array.Empty<BlogBlock>();

    public string? Error { get; private set; }

    public void OnGet()
    {
        var readme = Path.Combine(_paths.Root, "README.md");
        if (!System.IO.File.Exists(readme))
        {
            Error = "找不到 README.md。";
            return;
        }

        Blocks = _blog.Render(System.IO.File.ReadAllText(readme));
    }
}
