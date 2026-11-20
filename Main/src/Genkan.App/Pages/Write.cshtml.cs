using Genkan.Blog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize(Roles = "admin")]
public class WriteModel : PageModel
{
    private readonly BlogService _blog;

    public WriteModel(BlogService blog) => _blog = blog;

    [BindProperty]
    public string Slug { get; set; } = string.Empty;

    [BindProperty]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string Summary { get; set; } = string.Empty;

    [BindProperty]
    public string Tags { get; set; } = string.Empty;

    [BindProperty]
    public string Visibility { get; set; } = "public";

    [BindProperty]
    public bool Draft { get; set; }

    [BindProperty]
    public string Body { get; set; } = string.Empty;

    public string? Error { get; private set; }

    public bool IsEdit => !string.IsNullOrWhiteSpace(Slug);

    public void OnGet(string? slug = null)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return;
        }

        var post = _blog.Find(slug);
        if (post is null)
        {
            return;
        }

        Slug = post.Slug;
        Title = post.Title;
        Summary = post.Summary;
        Tags = string.Join(", ", post.Tags);
        Visibility = post.Visibility;
        Draft = post.Draft;
        Body = post.Body;
    }

    public IActionResult OnPost()
    {
        var slug = (Slug ?? string.Empty).Trim().ToLowerInvariant();

        if (!BlogService.IsValidSlug(slug))
        {
            Error = "Slug 只能是小写字母、数字和短横线，1–64 位。";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            Error = "标题不能为空。";
            return Page();
        }

        _blog.Save(
            slug,
            Title,
            Summary ?? string.Empty,
            (Tags ?? string.Empty).Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            BlogService.NormalizeVisibility(Visibility),
            Draft,
            Body ?? string.Empty);

        return Redirect($"/blog/{slug}");
    }
}
