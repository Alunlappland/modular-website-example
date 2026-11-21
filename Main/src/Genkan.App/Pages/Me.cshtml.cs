using System.Security.Claims;
using Genkan.Data;
using Genkan.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class MeModel : PageModel
{
    private readonly UserRepository _users;

    public MeModel(UserRepository users) => _users = users;

    public UserRecord? Me { get; private set; }

    public string CreatedText { get; private set; } = "—";

    public string LastLoginText { get; private set; } = "—";

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    [BindProperty]
    public string? OldPassword { get; set; }

    [BindProperty]
    public string? NewPassword { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

    public void OnGet()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Me = string.IsNullOrWhiteSpace(id) ? null : _users.FindById(id);

        if (Me is null)
        {
            return;
        }

        CreatedText = Format(Me.CreatedAt);
        LastLoginText = Format(Me.LastLoginAt);
    }

    public IActionResult OnPostChangePassword()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Me = string.IsNullOrWhiteSpace(id) ? null : _users.FindById(id);

        if (Me is null)
        {
            return Forbid();
        }

        CreatedText = Format(Me.CreatedAt);
        LastLoginText = Format(Me.LastLoginAt);

        var oldPassword = OldPassword ?? string.Empty;
        var newPassword = NewPassword ?? string.Empty;

        if (!PasswordHasher.Verify(oldPassword, Me.PwdSalt, Me.PwdHash, Me.PwdIter))
        {
            Error = "当前密码不对。";
            return Page();
        }

        if (newPassword.Length < 10)
        {
            Error = "新密码至少 10 位。";
            return Page();
        }

        if (!string.Equals(newPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            Error = "两次输入的新密码不一样。";
            return Page();
        }

        if (string.Equals(oldPassword, newPassword, StringComparison.Ordinal))
        {
            Error = "新密码不能和旧密码一样。";
            return Page();
        }

        var (salt, hash, iterations) = PasswordHasher.Create(newPassword);
        _users.UpdatePassword(Me.Id, salt, hash, iterations);

        Message = "密码已更新，下次登录用新密码。";
        OldPassword = NewPassword = ConfirmPassword = null;
        return Page();
    }

    private static string Format(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || !DateTimeOffset.TryParse(iso, out var value))
        {
            return "—";
        }

        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
