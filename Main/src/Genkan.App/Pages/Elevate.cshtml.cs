using System.Security.Claims;
using Genkan.Backup;
using Genkan.Data;
using Genkan.Security;
using Genkan.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class ElevateModel : PageModel
{
    private readonly UserRepository _users;
    private readonly SecretStore _secrets;
    private readonly ElevationService _elevation;

    public ElevateModel(UserRepository users, SecretStore secrets, ElevationService elevation)
    {
        _users = users;
        _secrets = secrets;
        _elevation = elevation;
    }

    public bool TotpEnabled => _secrets.Get("totp.enabled") == "1";

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public bool Ring { get; private set; }

    public string? ReturnUrl { get; private set; }

    [BindProperty]
    public string? Code { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public IActionResult OnPost(string? returnUrl)
    {
        ReturnUrl = returnUrl;

        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var user = string.IsNullOrWhiteSpace(id) ? null : _users.FindById(id);
        if (user is null)
        {
            return Forbid();
        }

        var secret = _secrets.Get("totp.secret");

        if (TotpEnabled && secret is not null)
        {
            if (!Totp.Validate(secret, Code))
            {
                Error = "验证码不对。";
                return Page();
            }
        }
        else if (!PasswordHasher.Verify(Password ?? string.Empty, user.PwdSalt, user.PwdHash, user.PwdIter))
        {
            Error = "密码不对。";
            return Page();
        }

        var expires = _elevation.Elevate(HttpContext, user.Id);
        Message = "已提权，到 " + expires.ToLocalTime().ToString("HH:mm") + " 自动失效。";

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Page();
    }

    public IActionResult OnPostRing()
    {
        Ring = true;
        AudioAlert.Ping();
        Message = "已在服务器上响铃（服务运行在后台会话时可能听不到，浏览器这边也会响）。";
        return Page();
    }
}
