using System.Security.Claims;
using Genkan.Files;
using Genkan.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages.Files;

[Authorize]
public class IndexModel : PageModel
{
    private readonly FileRepository _repository;
    private readonly FileStorage _storage;
    private readonly ShareService _shares;
    private readonly AuditLog _audit;
    private readonly GenkanPaths _paths;

    public IndexModel(FileRepository repository, FileStorage storage, ShareService shares, AuditLog audit, GenkanPaths paths)
    {
        _repository = repository;
        _storage = storage;
        _shares = shares;
        _audit = audit;
        _paths = paths;
    }

    public IReadOnlyList<FileRow> Files { get; private set; } = Array.Empty<FileRow>();

    public IReadOnlyList<ShareRow> Shares { get; private set; } = Array.Empty<ShareRow>();

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public List<IFormFile>? Uploads { get; set; }

    [BindProperty]
    public IFormFile? Chunk { get; set; }

    public int FileCount => Files.Count;

    [BindProperty]
    public int? ShareDays { get; set; } = 7;

    [BindProperty]
    public int? MaxDownloads { get; set; }

    [BindProperty]
    public string? SharePassword { get; set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public string? NewLink { get; private set; }

    private string OwnerId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";

    public void OnGet() => Load();

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        var incoming = new List<IFormFile>();
        if (Uploads is { Count: > 0 })
        {
            incoming.AddRange(Uploads);
        }
        if (Upload is { Length: > 0 })
        {
            incoming.Add(Upload);
        }

        incoming = incoming.Where(file => file.Length > 0).ToList();
        var wantsJson = (Request.Headers.Accept.ToString() ?? string.Empty)
            .Contains("application/json", StringComparison.OrdinalIgnoreCase);

        if (incoming.Count == 0)
        {
            Error = "没有选择文件。";
            Load();
            return wantsJson ? new JsonResult(new { ok = false, message = Error }) : Page();
        }

        var owner = OwnerId;
        var saved = 0;
        var failed = new List<string>();

        foreach (var file in incoming)
        {
            try
            {
                if (file.Length > 512L * 1024 * 1024)
                {
                    failed.Add(file.FileName + "（超过 512MB）");
                    continue;
                }

                var relative = await _storage.SaveUploadAsync(owner, file, file.FileName, cancellationToken);
                var full = _storage.FullPath(owner, relative);
                var sha = await FileStorage.ComputeSha256Async(full, cancellationToken);

                _repository.AddFile(new FileRow
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Owner = owner,
                    Name = relative,
                    RelPath = relative,
                    Size = new FileInfo(full).Length,
                    Sha256 = sha,
                    Mime = file.ContentType,
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                });

                saved++;
            }
            catch (Exception ex)
            {
                failed.Add(file.FileName + "：" + ex.Message);
            }
        }

        _audit.Write(HttpContext, "files.upload", saved + " files", failed.Count == 0 ? "ok" : "partial");
        Message = saved + " 个文件已上传" + (failed.Count > 0 ? $"，{failed.Count} 个失败" : string.Empty);

        if (failed.Count > 0)
        {
            Error = string.Join("；", failed.Take(3));
        }

        Load();

        return wantsJson
            ? new JsonResult(new { ok = failed.Count == 0, message = Message, saved, failed = failed.Take(5).ToArray() })
            : Page();
    }

    public bool IsMissing(FileRow file) => !_storage.Exists(file);

    private string? ChunkDirectory(string uploadId)
    {
        if (!Guid.TryParse(uploadId, out var id))
        {
            return null;
        }

        return Path.Combine(_paths.Uploads, id.ToString("N"));
    }

    /// <summary>接收一个分块（默认 2MB），按偏移追加到临时文件。</summary>
    public async Task<IActionResult> OnPostChunkAsync(string uploadId, long offset, CancellationToken cancellationToken)
    {
        var directory = ChunkDirectory(uploadId);
        if (directory is null || Chunk is null || Chunk.Length == 0)
        {
            return new JsonResult(new { ok = false, error = "参数不对" }) { StatusCode = 400 };
        }

        Directory.CreateDirectory(directory);
        var part = Path.Combine(directory, "data.part");
        var existing = System.IO.File.Exists(part) ? new FileInfo(part).Length : 0;

        if (offset != existing)
        {
            // 客户端断点续传：告诉它服务器已有的字节数
            return new JsonResult(new { ok = false, error = "offset-mismatch", expected = existing }) { StatusCode = 409 };
        }

        await using (var stream = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await Chunk.CopyToAsync(stream, cancellationToken);
        }

        return new JsonResult(new { ok = true, received = new FileInfo(part).Length });
    }

    /// <summary>所有分块到齐后，把临时文件落到用户目录并登记。</summary>
    public async Task<IActionResult> OnPostCompleteAsync(string uploadId, string fileName, long totalBytes, CancellationToken cancellationToken)
    {
        var directory = ChunkDirectory(uploadId);
        if (directory is null)
        {
            return new JsonResult(new { ok = false, error = "参数不对" }) { StatusCode = 400 };
        }

        var part = Path.Combine(directory, "data.part");
        if (!System.IO.File.Exists(part))
        {
            return new JsonResult(new { ok = false, error = "没有收到任何分块" }) { StatusCode = 400 };
        }

        var actual = new FileInfo(part).Length;
        if (totalBytes > 0 && actual != totalBytes)
        {
            return new JsonResult(new { ok = false, error = "大小不一致", expected = totalBytes, actual }) { StatusCode = 409 };
        }

        var owner = OwnerId;
        var relative = _storage.FinishChunkedUpload(owner, fileName, part);
        var full = _storage.FullPath(owner, relative);
        var sha = await FileStorage.ComputeSha256Async(full, cancellationToken);

        _repository.AddFile(new FileRow
        {
            Id = Guid.NewGuid().ToString("N"),
            Owner = owner,
            Name = relative,
            RelPath = relative,
            Size = new FileInfo(full).Length,
            Sha256 = sha,
            Mime = "application/octet-stream",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        });

        try { Directory.Delete(directory, recursive: true); } catch { }
        _audit.Write(HttpContext, "files.upload", relative, "ok");

        return new JsonResult(new { ok = true, name = relative, size = new FileInfo(full).Length });
    }

    public IActionResult OnPostPrune()
    {
        var owner = OwnerId;
        var removed = 0;

        foreach (var file in _repository.ListFiles(owner))
        {
            if (_storage.Exists(file))
            {
                continue;
            }

            _repository.DeleteSharesForFile(file.Id, owner);
            _repository.DeleteFile(file.Id, owner);
            removed++;
        }

        _audit.Write(HttpContext, "files.prune", removed.ToString(), "ok");
        Message = removed == 0 ? "没有需要清理的条目。" : $"已清理 {removed} 条失效记录（文件已不在磁盘上）。";
        Load();
        return Page();
    }

    public IActionResult OnGetDownload(string id)
    {
        var file = _repository.GetFile(id);
        if (file is null || file.Owner != OwnerId)
        {
            return NotFound();
        }

        var full = _storage.FullPath(file.Owner, file.RelPath);
        if (!System.IO.File.Exists(full))
        {
            return NotFound();
        }

        return new PhysicalFileResult(full, string.IsNullOrWhiteSpace(file.Mime) ? "application/octet-stream" : file.Mime)
        {
            FileDownloadName = file.Name,
            EnableRangeProcessing = true,
        };
    }

    public IActionResult OnPostDelete(string id)
    {
        var file = _repository.GetFile(id);
        if (file is null || file.Owner != OwnerId)
        {
            Error = "找不到这个文件。";
            Load();
            return Page();
        }

        try
        {
            var full = _storage.FullPath(file.Owner, file.RelPath);
            if (System.IO.File.Exists(full))
            {
                System.IO.File.Delete(full);
            }
        }
        catch
        {
            // 物理文件删不掉时仍然移除索引
        }

        _repository.DeleteFile(id, OwnerId);
        _audit.Write(HttpContext, "files.delete", file.Name, "ok");
        Message = "已删除：" + file.Name;
        Load();
        return Page();
    }

    public IActionResult OnPostShare(string id)
    {
        var file = _repository.GetFile(id);
        if (file is null || file.Owner != OwnerId)
        {
            Error = "找不到这个文件。";
            Load();
            return Page();
        }

        if (!_storage.Exists(file))
        {
            Error = "文件已不在磁盘上，无法生成分享链接。";
            Load();
            return Page();
        }

        var share = _shares.CreateFileShare(OwnerId, file.Id, ShareDays, MaxDownloads, SharePassword);        NewLink = "/s/" + share.Token;
        _audit.Write(HttpContext, "files.share", file.Name, "ok");
        Message = "分享链接已生成。";
        Load();
        return Page();
    }

    public IActionResult OnPostDrop()
    {
        var share = _shares.CreateDrop(OwnerId, ShareDays, SharePassword);
        NewLink = "/d/" + share.Token;
        _audit.Write(HttpContext, "files.drop", share.Token, "ok");
        Message = "收件箱链接已生成。";
        Load();
        return Page();
    }

    public IActionResult OnPostRevoke(string id)
    {
        if (_repository.DeleteShare(id, OwnerId))
        {
            Message = "分享已撤销。";
        }

        Load();
        return Page();
    }

    private void Load()
    {
        Files = _repository.ListFiles(OwnerId);
        Shares = _repository.ListShares(OwnerId);
    }
}
