using Genkan.Data;
using Genkan.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

public class LoginModel : PageModel
{
    private readonly UserRepository _users;
    private readonly AuditLog _audit;

    public LoginModel(UserRepository users, AuditLog audit)
    {
        _users = users;
        _audit = audit;
    }

    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    public string? Error { get; private set; }

    public string ReturnUrl { get; private set; } = "/";

    public IActionResult OnGet(string? returnUrl = null, bool denied = false)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(Safe(returnUrl));
        }

        ReturnUrl = Safe(returnUrl);
        if (denied)
        {
            Error = "当前账号没有访问该页面的权限。";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = Safe(returnUrl);

        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password))
        {
            Error = "用户名和密码都要填。";
            return Page();
        }

        var key = (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "?") + "|" + UserName.Trim().ToLowerInvariant();
        if (LoginThrottle.IsLocked(key, out var waitSeconds))
        {
            Error = $"尝试次数太多，请 {waitSeconds} 秒后再试。";
            _audit.Write(HttpContext, "login.locked", UserName, "blocked");
            return Page();
        }

        var user = _users.FindByName(UserName.Trim());
        if (user is null || !user.Enabled ||
            !PasswordHasher.Verify(Password, user.PwdSalt, user.PwdHash, user.PwdIter))
        {
            LoginThrottle.Fail(key);
            Error = "用户名或密码不对。";
            _audit.Write(HttpContext, "login.failed", UserName, "fail");
            return Page();
        }

        LoginThrottle.Reset(key);
        await SignInHelper.SignInAsync(HttpContext, user);
        _users.TouchLogin(user.Id);
        _audit.Write(HttpContext, "login.ok", user.Name, "ok");

        return LocalRedirect(ReturnUrl);
    }

    private static string Safe(string? url)
        => string.IsNullOrWhiteSpace(url) || !url.StartsWith('/') || url.StartsWith("//")
            ? "/"
            : url;
}
