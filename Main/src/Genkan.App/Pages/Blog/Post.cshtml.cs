using Genkan.Blog;
using Genkan.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages.Blog;

public class PostModel : PageModel
{
    private readonly BlogService _blog;
    private readonly BlogRepository _repository;
    private readonly ModerationService _moderation;
    private readonly AuditLog _audit;

    public PostModel(BlogService blog, BlogRepository repository, ModerationService moderation, AuditLog audit)
    {
        _blog = blog;
        _repository = repository;
        _moderation = moderation;
        _audit = audit;
    }

    public BlogPost Post { get; private set; } = default!;

    public IReadOnlyList<BlogBlock> Blocks { get; private set; } = Array.Empty<BlogBlock>();

    public IReadOnlyDictionary<int, List<AnnotationRow>> Annotations { get; private set; }
        = new Dictionary<int, List<AnnotationRow>>();

    public IReadOnlyList<CommentRow> Comments { get; private set; } = Array.Empty<CommentRow>();

    public ReactionCounts Reactions { get; private set; } = new(0, 0);

    public bool IsAdmin { get; private set; }

    [BindProperty]
    public string CommentAuthor { get; set; } = string.Empty;

    [BindProperty]
    public string CommentBody { get; set; } = string.Empty;

    public string? Error { get; private set; }

    public IActionResult OnGet(string slug)
    {
        IsAdmin = User.IsInRole("admin");
        var post = _blog.Find(slug);

        if (post is null || !CanSee(post))
        {
            return NotFound();
        }

        Post = post;
        Load();
        return Page();
    }

    public async Task<IActionResult> OnPostCommentAsync(string slug)
    {
        IsAdmin = User.IsInRole("admin");
        var post = _blog.Find(slug);

        if (post is null || !CanSee(post))
        {
            return NotFound();
        }

        Post = post;
        var author = (CommentAuthor ?? string.Empty).Trim();
        var body = (CommentBody ?? string.Empty).Trim();

        if (author.Length is < 1 or > 24 || body.Length is < 1 or > 1000)
        {
            Error = "昵称 1–24 字，内容 1–1000 字。";
            Load();
            return Page();
        }

        if (!RateLimiter.TryAcquire(RateLimiter.ClientKey(HttpContext, "comment"), 5, TimeSpan.FromMinutes(5)))
        {
            Error = "评论太频繁，歇一会儿再试。";
            Load();
            return Page();
        }

        var status = User.Identity?.IsAuthenticated == true
            ? "approved"
            : await _moderation.ReviewAsync(author + "：" + body);

        _repository.AddComment(slug, author, body, status);
        _audit.Write(HttpContext, "blog.comment", slug, status);
        return Redirect($"/blog/{slug}#comments");
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

        if (post.Visibility == "public")
        {
            return true;
        }

        return post.Visibility == "member" && User.IsInRole("member");
    }

    private void Load()
    {
        Blocks = _blog.Render(Post.Body);
        Comments = _repository.Comments(Post.Slug, IsAdmin);
        Reactions = _repository.Counts(Post.Slug);

        var annotations = _repository.Annotations(Post.Slug, IsAdmin);
        Annotations = annotations
            .GroupBy(a => a.BlockIndex)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}
