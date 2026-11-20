using System.Security.Cryptography;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Genkan.Blog;

public sealed record BlogPost(
    string Slug,
    string Title,
    DateTimeOffset Date,
    string Summary,
    IReadOnlyList<string> Tags,
    string Visibility,
    bool Draft,
    string Body,
    string FilePath);

public sealed record BlogBlock(int Index, string Hash, string Html);

/// <summary>
/// 博客文章以 Markdown 文件为准（Main\document\blog\*.md），front-matter 描述元信息。
/// 渲染用 Markdig，按顶层块切开，每块带稳定的 hash 供行级注释锚定。
/// </summary>
public sealed class BlogService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers()
        .Build();

    private readonly GenkanPaths _paths;
    private readonly ILogger<BlogService> _logger;

    public BlogService(GenkanPaths paths, ILogger<BlogService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string Root => Path.Combine(_paths.Document, "blog");

    public IReadOnlyList<BlogPost> All()
    {
        Directory.CreateDirectory(Root);
        var posts = new List<BlogPost>();

        foreach (var file in Directory.EnumerateFiles(Root, "*.md"))
        {
            try
            {
                var post = Load(file);
                if (post is not null)
                {
                    posts.Add(post);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "文章解析失败：{File}", file);
            }
        }

        return posts.OrderByDescending(p => p.Date).ToList();
    }

    public BlogPost? Find(string slug)
    {
        if (!IsValidSlug(slug))
        {
            return null;
        }

        var file = Path.Combine(Root, slug + ".md");
        return File.Exists(file) ? Load(file) : null;
    }

    public IReadOnlyList<BlogBlock> Render(string markdown)
    {
        var blocks = new List<BlogBlock>();
        var document = Markdown.Parse(markdown ?? string.Empty, Pipeline);
        var index = 0;

        foreach (var block in document)
        {
            var html = RenderBlock(block, Pipeline);
            if (string.IsNullOrWhiteSpace(html))
            {
                continue;
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(html));
            var hash = Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);

            blocks.Add(new BlogBlock(index++, hash, html));
        }

        return blocks;
    }

    private static string RenderBlock(Block block, MarkdownPipeline pipeline)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(block);
        writer.Flush();
        return writer.ToString();
    }

    public void Save(
        string slug,
        string title,
        string summary,
        IEnumerable<string> tags,
        string visibility,
        bool draft,
        string body,
        DateTimeOffset? date = null)
    {
        if (!IsValidSlug(slug))
        {
            throw new ArgumentException("slug 只能是小写字母、数字和短横线。", nameof(slug));
        }

        Directory.CreateDirectory(Root);

        var front = new StringBuilder();
        front.AppendLine("---");
        front.AppendLine($"title: {title.Trim()}");
        front.AppendLine($"date: {(date ?? DateTimeOffset.Now).ToString("yyyy-MM-dd")}");
        front.AppendLine($"summary: {summary.Trim()}");
        front.AppendLine($"tags: {string.Join(", ", tags.Select(t => t.Trim()).Where(t => t.Length > 0))}");
        front.AppendLine($"visibility: {NormalizeVisibility(visibility)}");
        front.AppendLine($"draft: {(draft ? "true" : "false")}");
        front.AppendLine("---");
        front.AppendLine();
        front.Append(body ?? string.Empty);

        File.WriteAllText(Path.Combine(Root, slug + ".md"), front.ToString(), Encoding.UTF8);
        _logger.LogInformation("文章已保存：{Slug}", slug);
    }

    public bool Delete(string slug)
    {
        if (!IsValidSlug(slug))
        {
            return false;
        }

        var file = Path.Combine(Root, slug + ".md");
        if (!File.Exists(file))
        {
            return false;
        }

        File.Delete(file);
        return true;
    }

    public static string NormalizeVisibility(string? visibility) => (visibility ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "member" => "member",
        "private" => "private",
        _ => "public",
    };

    public static bool IsValidSlug(string? slug)
        => !string.IsNullOrWhiteSpace(slug)
           && System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{0,63}$");

    // ---------- 内部 ----------
    private BlogPost? Load(string file)
    {
        var raw = File.ReadAllText(file);
        var (meta, body) = SplitFrontMatter(raw);
        var slug = Path.GetFileNameWithoutExtension(file);

        meta.TryGetValue("title", out var title);
        meta.TryGetValue("summary", out var summary);
        meta.TryGetValue("tags", out var tags);
        meta.TryGetValue("visibility", out var visibility);
        meta.TryGetValue("draft", out var draftText);
        meta.TryGetValue("date", out var dateText);

        var date = DateTimeOffset.TryParse(dateText, out var parsed) ? parsed : File.GetLastWriteTime(file);

        return new BlogPost(
            Slug: slug,
            Title: string.IsNullOrWhiteSpace(title) ? slug : title!,
            Date: date,
            Summary: summary ?? string.Empty,
            Tags: (tags ?? string.Empty)
                .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            Visibility: NormalizeVisibility(visibility),
            Draft: string.Equals(draftText, "true", StringComparison.OrdinalIgnoreCase),
            Body: body,
            FilePath: file);
    }

    private static (Dictionary<string, string> Meta, string Body) SplitFrontMatter(string raw)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var normalized = raw.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (meta, raw);
        }

        var end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0)
        {
            return (meta, raw);
        }

        var header = normalized.Substring(4, end - 4);
        var body = normalized[(end + 4)..].TrimStart('\n');

        foreach (var line in header.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim().Trim('"');
            if (key.Length > 0)
            {
                meta[key] = value;
            }
        }

        return (meta, body);
    }
}
