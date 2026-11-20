using Genkan.Files;
using Genkan.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

public class DropModel : PageModel
{
    private readonly ShareService _shares;
    private readonly FileStorage _storage;
    private readonly AuditLog _audit;

    public DropModel(ShareService shares, FileStorage storage, AuditLog audit)
    {
        _shares = shares;
        _storage = storage;
        _audit = audit;
    }

    public ShareRow? Share { get; private set; }

    public bool NeedPassword { get; private set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    public IActionResult OnGet(string token)
    {
        var share = _shares.Resolve(token);
        if (share is null || share.Kind != "drop")
        {
            return NotFound();
        }

        Share = share;
        NeedPassword = !string.IsNullOrEmpty(share.PasswordHash);
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(string token, CancellationToken cancellationToken)
    {
        var share = _shares.Resolve(token);
        if (share is null || share.Kind != "drop")
        {
            return NotFound();
        }

        Share = share;
        NeedPassword = !string.IsNullOrEmpty(share.PasswordHash);

        if (!_shares.CheckPassword(share, Password))
        {
            Error = "提取码不对。";
            return Page();
        }

        if (Upload is null || Upload.Length == 0)
        {
            Error = "没有选择文件。";
            return Page();
        }

        if (Upload.Length > 512L * 1024 * 1024)
        {
            Error = "单个文件最大 512MB。";
            return Page();
        }

        var relative = await _storage.SaveToDropAsync(share, Upload, cancellationToken);
        _audit.Write(HttpContext, "files.drop.receive", share.Token, "ok", who: "anonymous", detail: relative);
        Message = "已收到，谢谢。";
        return Page();
    }
}
