using System.Text;

namespace Genkan.Steward;

/// <summary>
/// 给代理站长的「项目资料」：站点是什么、目录怎么摆、有哪些页面、
/// 以及一份可检索的文档索引（README / Docs / 博客原文）。
/// 只读，且限制在固定几个目录里。
/// </summary>
public sealed class StewardKnowledge
{
    private readonly GenkanPaths _paths;
    private readonly IConfiguration _config;

    public StewardKnowledge(GenkanPaths paths, IConfiguration config)
    {
        _paths = paths;
        _config = config;
    }

    public sealed record DocEntry(string Name, string Path);

    public IReadOnlyList<DocEntry> Docs()
    {
        var list = new List<DocEntry>();

        void Add(string name, string path)
        {
            if (File.Exists(path))
            {
                list.Add(new DocEntry(name, path));
            }
        }

        Add("README", Path.Combine(_paths.Root, "README.md"));
        Add("DEVLOG", Path.Combine(_paths.Root, "DEVLOG.md"));

        if (Directory.Exists(_paths.Root + "\\Docs"))
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(_paths.Root, "Docs"), "*.md"))
            {
                Add("Docs/" + Path.GetFileNameWithoutExtension(file), file);
            }
        }

        foreach (var file in EnumerateMarkdown(Path.Combine(_paths.Document, "blog")))
        {
            Add("blog/" + Path.GetFileNameWithoutExtension(file), file);
        }

        foreach (var file in EnumerateMarkdown(Path.Combine(_paths.Document, "notes")))
        {
            Add("notes/" + Path.GetFileNameWithoutExtension(file), file);
        }

        return list.OrderBy(doc => doc.Name, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> EnumerateMarkdown(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.md")
            : Enumerable.Empty<string>();

    public string? Read(string name)
    {
        var doc = Docs().FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileNameWithoutExtension(item.Path), name, StringComparison.OrdinalIgnoreCase));

        if (doc is null)
        {
            return null;
        }

        var text = File.ReadAllText(doc.Path);
        return text.Length <= 20000 ? text : text[..20000] + "\n\n…（已截断）";
    }

    public string Search(string query, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "要搜什么？";
        }

        var results = new List<string>();

        foreach (var doc in Docs())
        {
            string text;
            try
            {
                text = File.ReadAllText(doc.Path);
            }
            catch
            {
                continue;
            }

            var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var start = Math.Max(0, index - 80);
            var length = Math.Min(240, text.Length - start);
            var snippet = text.Substring(start, length).Replace('\n', ' ').Trim();

            results.Add($"【{doc.Name}】{snippet}");

            if (results.Count >= limit)
            {
                break;
            }
        }

        return results.Count == 0 ? "没搜到「" + query + "」。" : string.Join("\n\n", results);
    }

    /// <summary>写进系统提示的紧凑资料，控制在几千字以内。</summary>
    public string BuildProfile()
    {
        var siteName = _config["site:name"] ?? "玄关";
        var builder = new StringBuilder();

        builder.AppendLine("【站点概况】");
        builder.AppendLine($"- 名字：{siteName}；程序叫「玄关 Genkan」，站长自己写的自托管个人门户。");
        builder.AppendLine("- 技术栈：.NET 10 / ASP.NET Core（Razor Pages + Minimal API）、SQLite + Dapper、Markdig、Serilog。");
        builder.AppendLine("- 运行方式：Windows 服务（nssm 注册），本地 localhost:8080，经 Cloudflare Tunnel 对外。");
        builder.AppendLine();
        builder.AppendLine("【目录约定】");
        builder.AppendLine("- Main/src：源码；Main/bin：部署产物；Main/conf：配置与密钥（不入库）");
        builder.AppendLine("- Main/data：上传文件、收件箱、对话文件；Main/database：genkan.db；Main/logs：日志");
        builder.AppendLine("- Main/document/blog：博客 Markdown；Main/document/notes：笔记 Markdown");
        builder.AppendLine("- Extend/<id>/extend.json：挂载物清单（static / iframe / link 三种）");
        builder.AppendLine();
        builder.AppendLine("【站点模块】");
        builder.AppendLine("- / 玄关首页；/ai 多供应商对话；/blog 博客（点赞、行级注释、评论审核）；/files 文件与分享链接；");
        builder.AppendLine("- /dashboard 仪表盘；/sync Syncthing 集成；/console 命令提示（只给用法不执行）；");
        builder.AppendLine("- /notes 笔记；/clip 跨设备剪贴板；/diagnostics 自诊断；/backup kopia 备份与 R2 镜像；");
        builder.AppendLine("- /admin/routes 路由管理、/admin/users 账号管理（均仅本机）；/elevate 临时提权；/me 账号与外观。");
        builder.AppendLine();
        builder.AppendLine("【权限与规则】");
        builder.AppendLine("- 角色：admin（全部）、member（chat/博客/files/sync/backup 查看）、guest（只读博客）。");
        builder.AppendLine("- 敏感操作（删快照、恢复、导出恢复包）必须先 /elevate 提权，15 分钟有效。");
        builder.AppendLine("- 备份：kopia 仓库 H:\\restore3，每天 12:00 增量、每 10 天全量，之后 rclone 镜像到 Cloudflare R2。");
        builder.AppendLine("- 你没有删除、恢复、备份的权限；写博客只会存草稿。");
        builder.AppendLine();
        builder.AppendLine("【文档索引】（可用 read_doc / search_docs 查看）");

        foreach (var doc in Docs().Take(40))
        {
            builder.AppendLine("- " + doc.Name);
        }

        return builder.ToString();
    }
}
