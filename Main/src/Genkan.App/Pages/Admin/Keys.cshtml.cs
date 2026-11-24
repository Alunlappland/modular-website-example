using Genkan.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages.Admin;

[Authorize(Roles = "admin")]
public class KeysModel : PageModel
{
    private readonly ApiKeyRepository _keys;

    public KeysModel(ApiKeyRepository keys) => _keys = keys;

    public IReadOnlyList<ApiKeyRow> Keys { get; private set; } = Array.Empty<ApiKeyRow>();

    public string? CreatedKey { get; private set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    [BindProperty]
    public string? Name { get; set; }

    [BindProperty]
    public List<string> Scopes { get; set; } = new();

    public IReadOnlyList<string> AllScopes => ApiScopes.All;

    public void OnGet()
    {
        CreatedKey = TempData["key.created"] as string;
        Message = TempData["key.message"] as string;
        Load();
    }

    public IActionResult OnPostCreate()
    {
        var name = (Name ?? string.Empty).Trim();
        if (name.Length is < 2 or > 40)
        {
            Error = "名称 2–40 个字符。";
            Load();
            return Page();
        }

        var scopes = Scopes.Where(scope => ApiScopes.All.Contains(scope)).Distinct().ToList();
        if (scopes.Count == 0)
        {
            Error = "至少选一个权限。";
            Load();
            return Page();
        }

        var key = _keys.Create(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown", name, scopes);
        TempData["key.created"] = key;
        TempData["key.message"] = "密钥只显示这一次，复制走。";
        return RedirectToPage();
    }

    public IActionResult OnPostRevoke(string id)
    {
        _keys.Revoke(id);
        TempData["key.message"] = "已吊销。";
        return RedirectToPage();
    }

    private void Load() => Keys = _keys.List();
}
