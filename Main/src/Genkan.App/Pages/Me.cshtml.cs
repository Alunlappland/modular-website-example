using System.Security.Claims;
using Genkan.Backup;
using Genkan.Data;
using Genkan.Security;
using Genkan.Theme;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class MeModel : PageModel
{
    private readonly UserRepository _users;
    private readonly SecretStore _secrets;
    private readonly ElevationService _elevation;
    private readonly ThemeService _theme;

    public MeModel(UserRepository users, SecretStore secrets, ElevationService elevation, ThemeService theme)
    {
        _users = users;
        _secrets = secrets;
        _elevation = elevation;
        _theme = theme;
    }

    public UserRecord? Me { get; private set; }

    public string CreatedText { get; private set; } = "—";

    public string LastLoginText { get; private set; } = "—";

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public bool TotpEnabled => _secrets.Get("totp.enabled") == "1";

    public string? TotpSecret => _secrets.Get("totp.secret");

    public string? OtpauthUri => TotpSecret is null
        ? null
        : Totp.BuildUri(TotpSecret, Me?.Name ?? "manifld", "玄关 Genkan");

    public bool Elevated => Me is not null && _elevation.IsElevated(HttpContext, Me.Id);

    public ThemeSettings ThemeSettings => _theme.Load();

    public bool HasBackground => _theme.HasBackground;

    public long BackgroundVersion => _theme.BackgroundVersion;

    [BindProperty]
    public string? OldPassword { get; set; }

    [BindProperty]
    public string? NewPassword { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

    [BindProperty]
    public string? TotpCode { get; set; }

    [BindProperty]
    public bool EffectsEnabled { get; set; }

    [BindProperty]
    public string FontChoice { get; set; } = "serif";

    [BindProperty]
    public bool InsetBackground { get; set; } = true;

    [BindProperty]
    public IFormFile? Background { get; set; }

    public void OnGet()
    {
        Load();
        EffectsEnabled = ThemeSettings.Effects;
        FontChoice = ThemeSettings.Font;
        InsetBackground = ThemeSettings.InsetBackground;
    }

    public IActionResult OnPostChangePassword()
    {
        Load();
        if (Me is null)
        {
            return Forbid();
        }

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

    public IActionResult OnPostTotpBegin()
    {
        Load();
        if (Me is null)
        {
            return Forbid();
        }

        var secret = Totp.GenerateSecret();
        _secrets.Set("totp.secret", secret);
        _secrets.Remove("totp.enabled");

        Message = "已生成密钥。把它加进验证器，再输入一次 6 位验证码确认。";
        return Page();
    }

    public IActionResult OnPostTotpConfirm()
    {
        Load();
        if (Me is null)
        {
            return Forbid();
        }

        var secret = _secrets.Get("totp.secret");
        if (string.IsNullOrWhiteSpace(secret))
        {
            Error = "还没有生成密钥。";
            return Page();
        }

        if (!Totp.Validate(secret, TotpCode))
        {
            Error = "验证码不对（注意时间同步）。";
            return Page();
        }

        _secrets.Set("totp.enabled", "1");
        Message = "二次验证已启用。之后提权需要验证器里的验证码。";
        return Page();
    }

    public IActionResult OnPostTotpDisable()
    {
        Load();
        if (Me is null)
        {
            return Forbid();
        }

        var secret = _secrets.Get("totp.secret");
        if (string.IsNullOrWhiteSpace(secret) || !Totp.Validate(secret, TotpCode))
        {
            Error = "验证码不对，无法关闭。";
            return Page();
        }

        _secrets.Remove("totp.secret");
        _secrets.Remove("totp.enabled");
        Message = "二次验证已关闭。";
        return Page();
    }

    public IActionResult OnPostTheme()
    {
        Load();
        if (Me is null)
        {
            return Forbid();
        }

        _theme.Save(new ThemeSettings
        {
            Effects = EffectsEnabled,
            Font = string.IsNullOrWhiteSpace(FontChoice) ? "serif" : FontChoice,
            InsetBackground = InsetBackground,
        });

        Message = "外观偏好已保存。";
        return Page();
    }

    public async Task<IActionResult> OnPostBackgroundAsync(CancellationToken cancellationToken)
    {
        Load();
        if (Me is null)
        {
            return Forbid();
        }

        if (Background is null)
        {
            Error = "没有选择图片。";
            return Page();
        }

        var (ok, message) = await _theme.SaveBackgroundAsync(Background, cancellationToken);
        if (ok) Message = message;
        else Error = message;

        return Page();
    }

    public IActionResult OnPostBackgroundRemove()
    {
        Load();
        if (Me is null)
        {
            return Forbid();
        }

        _theme.RemoveBackground();
        Message = "背景图已移除。";
        return Page();
    }

    public IActionResult OnPostDeElevate()
    {
        _elevation.Drop(HttpContext);
        Message = "已退出提权。";
        Load();
        return Page();
    }

    private void Load()
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

    private static string Format(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || !DateTimeOffset.TryParse(iso, out var value))
        {
            return "—";
        }

        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
