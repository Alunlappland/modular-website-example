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

    public string? Query { get; private set; }

    public void OnGet(string? q)
    {
        IsAdmin = User.IsInRole("admin");
        IsMember = IsAdmin || User.IsInRole("member");

        Query = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        var posts = _blog.All().Where(CanSee);

        if (Query is not null)
        {
            posts = posts.Where(post =>
                post.Title.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                post.Summary.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                post.Body.Contains(Query, StringComparison.OrdinalIgnoreCase) ||
                post.Tags.Any(tag => tag.Contains(Query, StringComparison.OrdinalIgnoreCase)));
        }

        Posts = posts.ToList();
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
