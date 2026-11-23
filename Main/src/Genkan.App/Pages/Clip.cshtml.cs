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

    [BindProperty]
    public bool Pin { get; set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public int RetainDays => _clips.RetainDays;

    private string OwnerId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    public void OnGet()
    {
        Message = TempData["clip.message"] as string;
        Error = TempData["clip.error"] as string;
        Load();
    }

    public IActionResult OnPostAdd()
    {
        var text = (Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            TempData["clip.error"] = "内容不能为空。";
            return RedirectToPage();
        }

        if (text.Length > 20000)
        {
            TempData["clip.error"] = "单条最多 20000 字。";
            return RedirectToPage();
        }

        _clips.Add(OwnerId, text, Pin);
        _audit.Write(HttpContext, "clip.add", text.Length + " chars" + (Pin ? " pinned" : ""), "ok");
        TempData["clip.message"] = Pin
            ? "已保存并加固：不会自动过期。"
            : $"已保存，{RetainDays} 天后自动过期（可以随时加固）。";
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(string id)
    {
        _clips.Delete(OwnerId, id);
        TempData["clip.message"] = "已删除。";
        return RedirectToPage();
    }

    public IActionResult OnPostPin(string id, bool pinned)
    {
        _clips.SetPinned(OwnerId, id, pinned);
        TempData["clip.message"] = pinned ? "已加固，不会自动过期。" : "已取消加固，按保留期过期。";
        return RedirectToPage();
    }

    public IActionResult OnPostClear()
    {
        var removed = _clips.Clear(OwnerId);
        _audit.Write(HttpContext, "clip.clear", removed + " items", "ok");
        TempData["clip.message"] = $"已清空 {removed} 条。";
        return RedirectToPage();
    }

    public int DaysLeft(ClipRow row)
    {
        if (row.Pinned)
        {
            return -1;
        }

        if (!DateTimeOffset.TryParse(row.CreatedAt, out var created))
        {
            return RetainDays;
        }

        var left = RetainDays - (int)Math.Floor((DateTimeOffset.UtcNow - created).TotalDays);
        return Math.Max(left, 0);
    }

    private void Load() => Items = _clips.List(OwnerId);
}
