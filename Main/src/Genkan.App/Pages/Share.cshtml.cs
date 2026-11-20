using Genkan.Files;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

public class ShareModel : PageModel
{
    private readonly ShareService _shares;
    private readonly FileRepository _repository;
    private readonly FileStorage _storage;

    public ShareModel(ShareService shares, FileRepository repository, FileStorage storage)
    {
        _shares = shares;
        _repository = repository;
        _storage = storage;
    }

    public ShareRow? Share { get; private set; }

    public FileRow? FileItem { get; private set; }

    public bool NeedPassword { get; private set; }

    public string? Error { get; private set; }

    [BindProperty]
    public string? Password { get; set; }

    public IActionResult OnGet(string token)
    {
        var share = _shares.Resolve(token);
        if (share is null)
        {
            return NotFound();
        }

        if (share.Kind == "drop")
        {
            return Redirect("/d/" + token);
        }

        Share = share;
        FileItem = share.FileId is null ? null : _repository.GetFile(share.FileId);
        if (FileItem is null)
        {
            return NotFound();
        }

        NeedPassword = !string.IsNullOrEmpty(share.PasswordHash);

        if (!_storage.Exists(FileItem))
        {
            Error = "文件已不在服务器上（可能已被删除），请联系分享者。";
        }

        return Page();
    }

    public IActionResult OnPostDownload(string token)
    {
        var share = _shares.Resolve(token);
        if (share is null || share.Kind != "share" || share.FileId is null)
        {
            return NotFound();
        }

        Share = share;
        FileItem = _repository.GetFile(share.FileId);
        if (FileItem is null)
        {
            return NotFound();
        }

        NeedPassword = !string.IsNullOrEmpty(share.PasswordHash);

        if (!_shares.CheckPassword(share, Password))
        {
            Error = "提取码不对。";
            return Page();
        }

        var full = _storage.FullPath(FileItem.Owner, FileItem.RelPath);
        if (!System.IO.File.Exists(full))
        {
            Error = "文件已不在服务器上（可能已被删除），请联系分享者。";
            return Page();
        }

        _repository.IncreaseDownload(share.Id);

        return new PhysicalFileResult(full, string.IsNullOrWhiteSpace(FileItem.Mime) ? "application/octet-stream" : FileItem.Mime)
        {
            FileDownloadName = FileItem.Name,
            EnableRangeProcessing = true,
        };
    }
}
