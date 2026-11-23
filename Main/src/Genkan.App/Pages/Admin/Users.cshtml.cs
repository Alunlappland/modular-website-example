using System.Security.Claims;
using Genkan.Data;
using Genkan.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages.Admin;

[Authorize(Roles = "admin")]
public class UsersModel : PageModel
{
    private readonly UserRepository _users;
    private readonly AuditLog _audit;

    public UsersModel(UserRepository users, AuditLog audit)
    {
        _users = users;
        _audit = audit;
    }

    public IReadOnlyList<UserRecord> Users { get; private set; } = Array.Empty<UserRecord>();

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public string MyId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

    [BindProperty]
    public string? NewName { get; set; }

    [BindProperty]
    public string? NewDisplay { get; set; }

    [BindProperty]
    public string? NewRole { get; set; } = "member";

    [BindProperty]
    public string? NewPassword { get; set; }

    public void OnGet() => Load();

    public IActionResult OnPostCreate()
    {
        Load();

        var name = (NewName ?? string.Empty).Trim();
        var password = NewPassword ?? string.Empty;

        if (name.Length < 2 || name.Length > 32)
        {
            Error = "用户名 2–32 个字符。";
            return Page();
        }

        if (password.Length < 10)
        {
            Error = "密码至少 10 位。";
            return Page();
        }

        if (_users.FindByName(name) is not null)
        {
            Error = "这个名字已经被占了。";
            return Page();
        }

        var role = NormalizeRole(NewRole);
        var (salt, hash, iterations) = PasswordHasher.Create(password);

        _users.Create(new UserRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Display = string.IsNullOrWhiteSpace(NewDisplay) ? name : NewDisplay.Trim(),
            Role = role,
            PwdSalt = salt,
            PwdHash = hash,
            PwdIter = iterations,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        });

        _audit.Write(HttpContext, "users.create", name + "/" + role, "ok");
        Message = "已创建：" + name + "（" + role + "）";
        return RedirectToPage();
    }

    public IActionResult OnPostRole(string id, string role)
    {
        Load();
        var target = _users.FindById(id);
        if (target is null)
        {
            Error = "找不到这个账号。";
            return Page();
        }

        var normalized = NormalizeRole(role);

        if (target.Id == MyId && normalized != "admin")
        {
            Error = "不能把自己的管理员角色去掉。";
            return Page();
        }

        if (target.Role == "admin" && normalized != "admin" && _users.CountAdmins() <= 1)
        {
            Error = "至少要留一个启用的管理员。";
            return Page();
        }

        _users.UpdateRole(target.Id, normalized);
        _audit.Write(HttpContext, "users.role", target.Name + "→" + normalized, "ok");
        Message = target.Name + " 的角色改成 " + normalized + "（对方需要重新登录才生效）。";
        return RedirectToPage();
    }

    public IActionResult OnPostToggle(string id)
    {
        Load();
        var target = _users.FindById(id);
        if (target is null)
        {
            Error = "找不到这个账号。";
            return Page();
        }

        if (target.Id == MyId)
        {
            Error = "不能禁用自己。";
            return Page();
        }

        if (target.Enabled && target.Role == "admin" && _users.CountAdmins() <= 1)
        {
            Error = "至少要留一个启用的管理员。";
            return Page();
        }

        _users.SetEnabled(target.Id, !target.Enabled);
        _audit.Write(HttpContext, "users.toggle", target.Name + (target.Enabled ? "→disabled" : "→enabled"), "ok");
        Message = target.Name + (target.Enabled ? " 已禁用（会话立即失效）。" : " 已启用。");
        return RedirectToPage();
    }

    public IActionResult OnPostResetPassword(string id, string password)
    {
        Load();
        var target = _users.FindById(id);
        if (target is null)
        {
            Error = "找不到这个账号。";
            return Page();
        }

        if (string.IsNullOrEmpty(password) || password.Length < 10)
        {
            Error = "新密码至少 10 位。";
            return Page();
        }

        var (salt, hash, iterations) = PasswordHasher.Create(password);
        _users.UpdatePassword(target.Id, salt, hash, iterations);
        _audit.Write(HttpContext, "users.password", target.Name, "ok");
        Message = "已重置 " + target.Name + " 的密码。";
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(string id)
    {
        Load();
        var target = _users.FindById(id);
        if (target is null)
        {
            Error = "找不到这个账号。";
            return Page();
        }

        if (target.Id == MyId)
        {
            Error = "不能删除自己。";
            return Page();
        }

        if (target.Role == "admin" && _users.CountAdmins() <= 1)
        {
            Error = "至少要留一个启用的管理员。";
            return Page();
        }

        _users.Delete(target.Id);
        _audit.Write(HttpContext, "users.delete", target.Name, "ok");
        Message = "已删除：" + target.Name;
        return RedirectToPage();
    }

    private static string NormalizeRole(string? role)
        => role is "admin" or "member" or "guest" or "viewer" ? role : "member";

    private void Load() => Users = _users.List();
}
