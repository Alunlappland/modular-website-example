using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Genkan.Ai;
using Genkan.Backup;
using Genkan.Blog;
using Genkan.Clip;
using Genkan.Files;
using Genkan.Notes;
using Genkan.Sys;

namespace Genkan.Steward;

public sealed record StewardResult(string Reply, IReadOnlyList<string> Trace, int Rounds);

/// <summary>
/// 代理站长：让模型通过工具调用读取/操作站点内容。
/// 工具全部走服务端，Key 不出服务器；破坏性操作（删除、恢复）不在工具集里。
/// </summary>
public sealed class StewardService
{
    private const int MaxRounds = 4;

    private readonly AiService _ai;
    private readonly IHttpClientFactory _http;
    private readonly DiagnosticsService _diagnostics;
    private readonly NotesService _notes;
    private readonly BlogService _blog;
    private readonly FileRepository _files;
    private readonly ClipRepository _clips;

    public StewardService(
        AiService ai,
        IHttpClientFactory http,
        DiagnosticsService diagnostics,
        NotesService notes,
        BlogService blog,
        FileRepository files,
        ClipRepository clips)
    {
        _ai = ai;
        _http = http;
        _diagnostics = diagnostics;
        _notes = notes;
        _blog = blog;
        _files = files;
        _clips = clips;
    }

    public async Task<StewardResult> RunAsync(
        AiProvider provider,
        IReadOnlyList<ChatMessage> history,
        string owner,
        CancellationToken cancellationToken)
    {
        var key = _ai.KeyOf(provider.Id);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new StewardResult($"{provider.Name} 还没有配置 API Key。", Array.Empty<string>(), 0);
        }

        var trace = new List<string>();

        var result = provider.Kind.ToLowerInvariant() switch
        {
            "anthropic" => await RunAnthropicAsync(provider, key, history, owner, trace, cancellationToken),
            _ => await RunOpenAiAsync(provider, key, history, owner, trace, cancellationToken),
        };

        return new StewardResult(result, trace, trace.Count);
    }

    // ---------------- OpenAI 兼容 ----------------

    private async Task<string> RunOpenAiAsync(
        AiProvider provider,
        string key,
        IReadOnlyList<ChatMessage> history,
        string owner,
        List<string> trace,
        CancellationToken cancellationToken)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt },
        };

        foreach (var message in history.TakeLast(12))
        {
            messages.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Content });
        }

        var tools = new JsonArray();
        foreach (var tool in Tools)
        {
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.Schema.DeepClone(),
                },
            });
        }

        var client = _http.CreateClient("ai");

        for (var round = 0; round < MaxRounds; round++)
        {
            var payload = new JsonObject
            {
                ["model"] = provider.Model,
                ["messages"] = messages.DeepClone(),
                ["tools"] = tools.DeepClone(),
                ["tool_choice"] = "auto",
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl}/chat/completions")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return $"模型返回 {(int)response.StatusCode}：{Trim(body)}";
            }

            using var document = JsonDocument.Parse(body);
            var message = document.RootElement.GetProperty("choices")[0].GetProperty("message");
            var content = message.TryGetProperty("content", out var textNode) ? textNode.GetString() : null;

            if (!message.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() == 0)
            {
                return string.IsNullOrWhiteSpace(content) ? "（模型没有返回内容）" : content;
            }

            messages.Add(new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = content,
                ["tool_calls"] = JsonNode.Parse(calls.GetRawText()),
            });

            foreach (var call in calls.EnumerateArray())
            {
                var name = call.GetProperty("function").GetProperty("name").GetString() ?? "";
                var arguments = call.GetProperty("function").GetProperty("arguments").GetString() ?? "{}";
                var id = call.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N");

                var output = await ExecuteAsync(name, arguments, owner, cancellationToken);
                trace.Add(name + " → " + FirstLine(output));

                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = id,
                    ["content"] = output,
                });
            }
        }

        return "工具调用轮数太多，先停一下。你可以把问题拆小一点再问。";
    }

    // ---------------- Anthropic ----------------

    private async Task<string> RunAnthropicAsync(
        AiProvider provider,
        string key,
        IReadOnlyList<ChatMessage> history,
        string owner,
        List<string> trace,
        CancellationToken cancellationToken)
    {
        var messages = new JsonArray();
        foreach (var message in history.TakeLast(12))
        {
            messages.Add(new JsonObject
            {
                ["role"] = message.Role == "assistant" ? "assistant" : "user",
                ["content"] = message.Content,
            });
        }

        var tools = new JsonArray();
        foreach (var tool in Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = tool.Schema.DeepClone(),
            });
        }

        var client = _http.CreateClient("ai");

        for (var round = 0; round < MaxRounds; round++)
        {
            var payload = new JsonObject
            {
                ["model"] = provider.Model,
                ["max_tokens"] = 4096,
                ["system"] = SystemPrompt,
                ["messages"] = messages.DeepClone(),
                ["tools"] = tools.DeepClone(),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl}/messages")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("anthropic-version", "2023-06-01");

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return $"模型返回 {(int)response.StatusCode}：{Trim(body)}";
            }

            using var document = JsonDocument.Parse(body);
            var blocks = document.RootElement.GetProperty("content");
            var text = new StringBuilder();
            var toolUses = new List<(string Id, string Name, string Input)>();

            foreach (var block in blocks.EnumerateArray())
            {
                var type = block.GetProperty("type").GetString();
                if (type == "text")
                {
                    text.Append(block.GetProperty("text").GetString());
                }
                else if (type == "tool_use")
                {
                    toolUses.Add((
                        block.GetProperty("id").GetString() ?? "",
                        block.GetProperty("name").GetString() ?? "",
                        block.GetProperty("input").GetRawText()));
                }
            }

            if (toolUses.Count == 0)
            {
                return text.Length == 0 ? "（模型没有返回内容）" : text.ToString();
            }

            var assistantBlocks = new JsonArray();
            if (text.Length > 0)
            {
                assistantBlocks.Add(new JsonObject { ["type"] = "text", ["text"] = text.ToString() });
            }

            foreach (var use in toolUses)
            {
                assistantBlocks.Add(new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = use.Id,
                    ["name"] = use.Name,
                    ["input"] = JsonNode.Parse(use.Input),
                });
            }

            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = assistantBlocks });

            var resultBlocks = new JsonArray();
            foreach (var use in toolUses)
            {
                var output = await ExecuteAsync(use.Name, use.Input, owner, cancellationToken);
                trace.Add(use.Name + " → " + FirstLine(output));

                resultBlocks.Add(new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = use.Id,
                    ["content"] = output,
                });
            }

            messages.Add(new JsonObject { ["role"] = "user", ["content"] = resultBlocks });
        }

        return "工具调用轮数太多，先停一下。";
    }

    // ---------------- 工具 ----------------

    private sealed record ToolDefinition(string Name, string Description, JsonObject Schema);

    private static readonly ToolDefinition[] Tools =
    {
        new("site_status", "查看站点体检结果：数据库、磁盘、备份、镜像、同步、AI、日志。",
            Schema()),
        new("list_notes", "列出服务器上的 Markdown 笔记。",
            Schema()),
        new("read_note", "读取一篇笔记的原文。",
            Schema(("name", "string", "笔记名，不含 .md"))),
        new("write_note", "新建或覆盖一篇笔记。",
            Schema(("name", "string", "笔记名，不含 .md"), ("content", "string", "Markdown 全文"))),
        new("list_posts", "列出博客文章。",
            Schema()),
        new("read_post", "读取一篇博客的原文。",
            Schema(("slug", "string", "文章 slug"))),
        new("write_post", "新建或覆盖一篇博客草稿（默认 draft=true，不会直接公开）。",
            Schema(("slug", "string", "文章 slug，例如 my-post"), ("title", "string", "标题"),
                ("content", "string", "Markdown 正文"))),
        new("list_files", "列出站长上传的最近文件。",
            Schema()),
        new("add_clip", "把一段文字存进跨设备剪贴板。",
            Schema(("text", "string", "要保存的文字"))),
    };

    private static JsonObject Schema(params (string Name, string Type, string Description)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();

        foreach (var (name, type, description) in properties)
        {
            props[name] = new JsonObject { ["type"] = type, ["description"] = description };
            required.Add(name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = required,
        };
    }

    private async Task<string> ExecuteAsync(string name, string arguments, string owner, CancellationToken cancellationToken)
    {
        JsonElement args;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
            args = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return "参数不是合法 JSON。";
        }

        string Arg(string key) => args.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";

        try
        {
            switch (name)
            {
                case "site_status":
                {
                    var checks = await _diagnostics.RunAsync(cancellationToken);
                    return string.Join("\n", checks.Select(check => $"[{check.Status}] {check.Name}：{check.Detail}"));
                }

                case "list_notes":
                {
                    var notes = _notes.List();
                    return notes.Count == 0
                        ? "还没有笔记。"
                        : string.Join("\n", notes.Select(note => $"{note.Name}（{note.Size} 字节，{note.UpdatedAt}）"));
                }

                case "read_note":
                    return _notes.Read(Arg("name")) ?? "找不到这篇笔记。";

                case "write_note":
                {
                    var noteName = NotesService.Normalize(Arg("name"));
                    if (noteName is null) return "笔记名不合法。";
                    _notes.Save(noteName, Arg("content"));
                    return "已保存笔记：" + noteName + ".md";
                }

                case "list_posts":
                {
                    var posts = _blog.All();
                    return posts.Count == 0
                        ? "还没有文章。"
                        : string.Join("\n", posts.Select(post =>
                            $"{post.Slug}｜{post.Title}｜{(post.Draft ? "草稿" : "已发布")}｜{post.Date:yyyy-MM-dd}"));
                }

                case "read_post":
                    return _blog.Find(Arg("slug"))?.Body ?? "找不到这篇文章。";

                case "write_post":
                {
                    var slug = Arg("slug");
                    if (!BlogService.IsValidSlug(slug)) return "slug 不合法（用英文、数字、连字符）。";
                    _blog.Save(slug, string.IsNullOrWhiteSpace(Arg("title")) ? slug : Arg("title"),
                        "", Array.Empty<string>(), "public", true, Arg("content"));
                    return "已保存为草稿：" + slug + "（draft=true）";
                }

                case "list_files":
                {
                    var files = _files.ListFiles(owner).Take(20).ToList();
                    return files.Count == 0
                        ? "还没有上传文件。"
                        : string.Join("\n", files.Select(file =>
                            $"{file.Name}（{file.Size / 1024.0 / 1024:F2} MB，{file.CreatedAt}）"));
                }

                case "add_clip":
                {
                    var text = Arg("text");
                    if (string.IsNullOrWhiteSpace(text)) return "内容是空的。";
                    _clips.Add(owner, text);
                    return "已存进剪贴板（" + text.Length + " 字）。";
                }

                default:
                    return "没有这个工具：" + name;
            }
        }
        catch (Exception ex)
        {
            return "工具执行失败：" + ex.Message;
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n')[0];
        return line.Length <= 80 ? line : line[..80] + "…";
    }

    private static string Trim(string text)
        => text.Length <= 300 ? text : text[..300];

    private const string SystemPrompt =
        "你是「玄关」站点的代理站长，替站长处理日常杂事。规则：\n" +
        "1. 需要事实时先调用工具，不要凭空猜站点状态。\n" +
        "2. 写笔记/写文章用工具，写完告诉站长写了什么。\n" +
        "3. 博客默认存草稿，不要声称已经公开发布。\n" +
        "4. 不能删除任何东西；遇到删除请求，说明你需要站长自己动手。\n" +
        "5. 回答用中文，简短直接，不要客套。";
}
