using System.Security.Claims;
using Genkan.Clip;
using Genkan.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class ClipModel : PageModel
{
    private readonly ClipRepository _clips;
    private readonly AuditLog _audit;

    public ClipModel(ClipRepository clips, AuditLog audit)
    {
        _clips = clips;
        _audit = audit;
    }

    public IReadOnlyList<ClipRow> Items { get; private set; } = Array.Empty<ClipRow>();

    [BindProperty]
    public string? Text { get; set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    private string OwnerId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    public void OnGet() => Load();

    public IActionResult OnPostAdd()
    {
        var text = (Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            Error = "内容不能为空。";
            Load();
            return Page();
        }

        if (text.Length > 20000)
        {
            Error = "单条最多 20000 字。";
            Load();
            return Page();
        }

        _clips.Add(OwnerId, text);
        _audit.Write(HttpContext, "clip.add", text.Length + " chars", "ok");
        Text = null;
        Message = "已保存，换设备登录也能看到。";
        Load();
        return Page();
    }

    public IActionResult OnPostDelete(string id)
    {
        _clips.Delete(OwnerId, id);
        Message = "已删除。";
        Load();
        return Page();
    }

    private void Load() => Items = _clips.List(OwnerId);
}
