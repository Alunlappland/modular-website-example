using Genkan.Blog;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages.Blog;

public class IndexModel : PageModel
{
    private readonly BlogService _blog;

    public IndexModel(BlogService blog) => _blog = blog;

    public IReadOnlyList<BlogPost> Posts { get; private set; } = Array.Empty<BlogPost>();

    public bool IsAdmin { get; private set; }

    public bool IsMember { get; private set; }

    public void OnGet()
    {
        IsAdmin = User.IsInRole("admin");
        IsMember = IsAdmin || User.IsInRole("member");

        Posts = _blog.All().Where(CanSee).ToList();
    }

    private bool CanSee(BlogPost post)
    {
        if (IsAdmin)
        {
            return true;
        }

        if (post.Draft)
        {
            return false;
        }

        return post.Visibility switch
        {
            "public" => true,
            "member" => IsMember,
            _ => false,
        };
    }
}
