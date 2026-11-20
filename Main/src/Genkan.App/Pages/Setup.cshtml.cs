using System.Text.RegularExpressions;
using Genkan.Data;
using Genkan.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

public partial class SetupModel : PageModel
{
    private readonly UserRepository _users;
    private readonly SetupState _setup;
    private readonly AuditLog _audit;

    public SetupModel(UserRepository users, SetupState setup, AuditLog audit)
    {
        _users = users;
        _setup = setup;
        _audit = audit;
    }

    [BindProperty]
    public string Display { get; set; } = "站长";

    [BindProperty]
    public string UserName { get; set; } = "manifld";

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string Confirm { get; set; } = string.Empty;

    public string? Error { get; private set; }

    public IActionResult OnGet() => _setup.Initialized ? Redirect("/login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (_setup.Initialized)
        {
            return Redirect("/login");
        }

        var display = (Display ?? string.Empty).Trim();
        var name = (UserName ?? string.Empty).Trim();

        if (display.Length is < 1 or > 32)
        {
            Error = "显示名要 1–32 个字。";
            return Page();
        }

        if (!NamePattern().IsMatch(name))
        {
            Error = "用户名只能是小写字母、数字、下划线或短横线，3–32 位。";
            return Page();
        }

        if (Password.Length < 10)
        {
            Error = "密码至少 10 位（建议 16 位以上，或一句只有你记得的话）。";
            return Page();
        }

        if (!string.Equals(Password, Confirm, StringComparison.Ordinal))
        {
            Error = "两次输入的密码不一样。";
            return Page();
        }

        var (salt, hash, iterations) = PasswordHasher.Create(Password);
        var record = new UserRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Display = display,
            Role = "admin",
            PwdAlgo = "pbkdf2-sha256",
            PwdSalt = salt,
            PwdHash = hash,
            PwdIter = iterations,
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            LastLoginAt = null,
        };

        _users.Create(record);
        _setup.Initialized = true;
        await SignInHelper.SignInAsync(HttpContext, record);
        _audit.Write(HttpContext, "setup.admin.created", record.Name, "ok", who: record.Name);

        return Redirect("/");
    }

    [GeneratedRegex("^[a-z0-9_-]{3,32}$")]
    private static partial Regex NamePattern();
}
