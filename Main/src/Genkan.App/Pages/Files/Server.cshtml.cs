using Genkan.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages.Files;

public sealed record ServerEntry(string Name, bool IsDirectory, long Size, string Relative);

[Authorize(Roles = "admin")]
public class ServerModel : PageModel
{
    private readonly FileStorage _storage;

    public ServerModel(FileStorage storage) => _storage = storage;

    public IReadOnlyList<(string Label, string Path)> Roots { get; private set; } = Array.Empty<(string, string)>();

    public int RootIndex { get; private set; }

    public string RelativePath { get; private set; } = string.Empty;

    public IReadOnlyList<ServerEntry> Entries { get; private set; } = Array.Empty<ServerEntry>();

    public string? Error { get; private set; }

    public string RootLabel => RootIndex >= 0 && RootIndex < Roots.Count ? Roots[RootIndex].Label : string.Empty;

    public void OnGet(int root = 0, string? path = null)
    {
        Roots = _storage.ServerRoots();
        if (Roots.Count == 0)
        {
            Error = "还没有配置可浏览的服务器目录（conf/appsettings.json → files:roots）。";
            return;
        }

        RootIndex = Math.Clamp(root, 0, Roots.Count - 1);
        RelativePath = (path ?? string.Empty).Replace('\\', '/').Trim('/');

        try
        {
            var full = _storage.ResolveServerPath(Roots[RootIndex].Path, RelativePath);
            if (!Directory.Exists(full))
            {
                Error = "目录不存在。";
                return;
            }

            var list = new List<ServerEntry>();

            foreach (var directory in Directory.EnumerateDirectories(full))
            {
                try
                {
                    var info = new DirectoryInfo(directory);
                    if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    list.Add(new ServerEntry(info.Name, true, 0, Join(RelativePath, info.Name)));
                }
                catch
                {
                    // 单个目录读不了就跳过
                }
            }

            foreach (var file in Directory.EnumerateFiles(full))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Attributes.HasFlag(FileAttributes.Hidden) || info.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    list.Add(new ServerEntry(info.Name, false, info.Length, Join(RelativePath, info.Name)));
                }
                catch
                {
                    // 跳过
                }
            }

            Entries = list
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public IActionResult OnGetDownload(int root, string path)
    {
        Roots = _storage.ServerRoots();
        if (root < 0 || root >= Roots.Count)
        {
            return NotFound();
        }

        try
        {
            var full = _storage.ResolveServerPath(Roots[root].Path, path);
            if (!System.IO.File.Exists(full))
            {
                return NotFound();
            }

            return new PhysicalFileResult(full, "application/octet-stream")
            {
                FileDownloadName = Path.GetFileName(full),
                EnableRangeProcessing = true,
            };
        }
        catch
        {
            return NotFound();
        }
    }

    public string ParentPath
    {
        get
        {
            if (string.IsNullOrEmpty(RelativePath))
            {
                return string.Empty;
            }

            var index = RelativePath.LastIndexOf('/');
            return index <= 0 ? string.Empty : RelativePath[..index];
        }
    }

    private static string Join(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : parent + "/" + name;
}
