using Genkan.Files;

namespace Genkan.Notes;

public sealed record NoteEntry(string Name, long Size, string UpdatedAt);

/// <summary>
/// 个人 Markdown 笔记：直接是 `Main/document/notes` 下的 .md 文件，
/// 可以用任何编辑器改、可以直接拷走，换界面样式也不影响。
/// </summary>
public sealed class NotesService
{
    private readonly GenkanPaths _paths;

    public NotesService(GenkanPaths paths) => _paths = paths;

    public string Root => Path.Combine(_paths.Document, "notes");

    public IReadOnlyList<NoteEntry> List()
    {
        if (!Directory.Exists(Root))
        {
            return Array.Empty<NoteEntry>();
        }

        return Directory
            .EnumerateFiles(Root, "*.md")
            .Select(file => new FileInfo(file))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new NoteEntry(
                Path.GetFileNameWithoutExtension(file.Name),
                file.Length,
                file.LastWriteTime.ToLocalTime().ToString("MM-dd HH:mm")))
            .ToList();
    }

    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(name.Trim());
        var safe = FileStorage.SafeFileName(stem);
        return string.IsNullOrWhiteSpace(safe) ? null : safe;
    }

    private string PathFor(string name) => Path.Combine(Root, name + ".md");

    public string? Read(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Save(string name, string content)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(PathFor(name), content);
    }

    public async Task<(bool Ok, string Message)> UploadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return (false, "没有选择文件。");
        }

        if (file.Length > 2 * 1024 * 1024)
        {
            return (false, "单个 Markdown 文件最大 2MB。");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".md", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "只接受 .md 文件。");
        }

        var name = Normalize(file.FileName);
        if (name is null)
        {
            return (false, "文件名不合法。");
        }

        Directory.CreateDirectory(Root);
        await using var stream = File.Create(PathFor(name));
        await file.CopyToAsync(stream, cancellationToken);

        return (true, "已导入：" + name + ".md");
    }

    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }
}
