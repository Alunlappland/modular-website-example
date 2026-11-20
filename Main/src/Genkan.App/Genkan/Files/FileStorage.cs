using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Genkan.Files;

/// <summary>
/// 文件落盘。用户文件在 <c>Main\data\files\&lt;owner&gt;\</c>；
/// 收件箱在 <c>Main\data\inbox\&lt;哈希目录&gt;\</c>——目录名用哈希，防止被猜到或遍历。
/// </summary>
public sealed class FileStorage
{
    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    private readonly GenkanPaths _paths;
    private readonly IConfiguration _config;
    private readonly ILogger<FileStorage> _logger;

    public FileStorage(GenkanPaths paths, IConfiguration config, ILogger<FileStorage> logger)
    {
        _paths = paths;
        _config = config;
        _logger = logger;
    }

    public string InboxRoot => Path.Combine(_paths.Data, "inbox");

    public string OwnerRoot(string owner) => Path.Combine(_paths.Data, "files", SafeSegment(owner));

    public async Task<string> SaveUploadAsync(string owner, IFormFile file, CancellationToken cancellationToken)
    {
        var dir = OwnerRoot(owner);
        Directory.CreateDirectory(dir);

        var name = SafeFileName(file.FileName);
        var relative = UniqueRelative(dir, name);
        var full = Path.Combine(dir, relative);

        await using (var stream = File.Create(full))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        _logger.LogInformation("文件已保存：{Owner}/{Relative}（{Size} 字节）", owner, relative, file.Length);
        return relative;
    }

    public async Task<string> SaveToDropAsync(ShareRow share, IFormFile file, CancellationToken cancellationToken)
    {
        var folder = string.IsNullOrWhiteSpace(share.Dir)
            ? Path.Combine(InboxRoot, SafeSegment(share.Token))
            : Path.Combine(InboxRoot, SafeSegment(share.Dir));

        Directory.CreateDirectory(folder);

        var name = SafeFileName(file.FileName);
        var relative = UniqueRelative(folder, name);
        var full = Path.Combine(folder, relative);

        await using (var stream = File.Create(full))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        _logger.LogInformation("收件箱收到文件：{Folder}/{Relative}（{Size} 字节）", share.Dir, relative, file.Length);
        return relative;
    }

    public string FullPath(string owner, string relative)
    {
        var root = Path.GetFullPath(OwnerRoot(owner)).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径越界。");
        }

        return full;
    }

    public string DropFullPath(ShareRow share, string relative)
    {
        var folder = string.IsNullOrWhiteSpace(share.Dir)
            ? Path.Combine(InboxRoot, SafeSegment(share.Token))
            : Path.Combine(InboxRoot, SafeSegment(share.Dir));

        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative));

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径越界。");
        }

        return full;
    }

    public static async Task<string> ComputeSha256Async(string fullPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fullPath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string SafeFileName(string? name)
    {
        var value = Path.GetFileName(name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = "file";
        }

        foreach (var character in InvalidNameChars)
        {
            value = value.Replace(character, '_');
        }

        value = value.Trim('.', ' ');
        if (value.Length == 0)
        {
            value = "file";
        }

        return value.Length > 120 ? value[..120] : value;
    }

    public static string SafeSegment(string? value)
    {
        var cleaned = Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9._-]", "_");
        return cleaned.Length == 0 ? "unknown" : cleaned;
    }

    private static string UniqueRelative(string directory, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);

        if (!File.Exists(Path.Combine(directory, name)))
        {
            return name;
        }

        for (var index = 1; index < 1000; index++)
        {
            var candidate = $"{stem} ({index}){extension}";
            if (!File.Exists(Path.Combine(directory, candidate)))
            {
                return candidate;
            }
        }

        return $"{stem}-{Guid.NewGuid():N}{extension}";
    }

    // ---------- 服务器文件（只读） ----------
    public IReadOnlyList<(string Label, string Path)> ServerRoots()
    {
        var configured = _config.GetSection("files:roots").Get<string[]>() ?? Array.Empty<string>();
        var roots = new List<(string, string)>();

        foreach (var raw in configured)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(raw);
                if (Directory.Exists(full))
                {
                    roots.Add((Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)), full));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "服务器文件根目录无效：{Root}", raw);
            }
        }

        return roots;
    }

    public string ResolveServerPath(string rootPath, string? relative)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative ?? string.Empty));

        if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("路径越界。");
        }

        return full;
    }
}
