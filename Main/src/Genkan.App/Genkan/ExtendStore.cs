using System.Text.Json;
using System.Text.RegularExpressions;

namespace Genkan;

public sealed record MountInput(
    string Id,
    string Title,
    string Route,
    string Kind,
    string? Url,
    string? Permission,
    int Order,
    bool Enabled);

/// <summary>
/// Extend 挂载清单的读写。磁盘是唯一事实来源：每次保存都写回
/// <c>Extend\&lt;id&gt;\extend.json</c>，然后让注册表重新扫描。
/// </summary>
public sealed class ExtendStore
{
    private static readonly string[] ReservedRoutes =
    {
        "/", "/login", "/logout", "/setup", "/me", "/dashboard", "/admin",
        "/api", "/assets", "/healthz", "/theme", "/favicon", "/chat",
    };

    private readonly GenkanPaths _paths;
    private readonly ILogger<ExtendStore> _logger;

    public ExtendStore(GenkanPaths paths, ILogger<ExtendStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public IReadOnlyList<string> Save(MountInput input)
    {
        var errors = new List<string>();

        if (!Regex.IsMatch(input.Id ?? string.Empty, "^[a-z0-9][a-z0-9-]{0,31}$"))
        {
            errors.Add("ID 只能是小写字母、数字、短横线，1–32 位。");
        }

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors.Add("标题不能为空。");
        }

        var route = NormalizeRoute(input.Route);
        if (route.Length < 2)
        {
            errors.Add("路由至少要有一个路径段，例如 /notes。");
        }

        if (ReservedRoutes.Contains(route, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"路由 {route} 被系统占用，换一个。");
        }

        var kind = (input.Kind ?? "static").Trim().ToLowerInvariant();
        if (kind is not ("static" or "iframe" or "link"))
        {
            errors.Add("类型只支持 static / iframe / link。");
        }

        if (kind is "iframe" or "link" && !IsSafeUrl(input.Url))
        {
            errors.Add("iframe / link 需要一个 http(s) 地址。");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var mountId = input.Id ?? string.Empty;
        var dir = Path.Combine(_paths.Extend, mountId);
        Directory.CreateDirectory(dir);

        if (kind == "static")
        {
            var www = Path.Combine(dir, "www");
            Directory.CreateDirectory(www);
            var index = Path.Combine(www, "index.html");
            if (!File.Exists(index))
            {
                File.WriteAllText(index,
                    "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">" +
                    $"<title>{System.Security.SecurityElement.Escape(input.Title)}</title></head>" +
                    "<body style=\"font-family:system-ui;padding:40px\"><h1>挂载点已创建</h1>" +
                    "<p>把静态文件放进这个目录的 <code>www/</code> 即可。</p></body></html>");
            }
        }

        var manifest = new
        {
            id = input.Id,
            title = input.Title.Trim(),
            route,
            kind,
            entry = "index.html",
            url = input.Url,
            permission = string.IsNullOrWhiteSpace(input.Permission) ? null : input.Permission.Trim(),
            order = input.Order,
            enabled = input.Enabled,
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, "extend.json"), json);

        _logger.LogInformation("挂载已保存：{Id} → {Route}（{Kind}）", input.Id, route, kind);
        return errors;
    }

    public bool Delete(string id)
    {
        if (!Regex.IsMatch(id ?? string.Empty, "^[a-z0-9][a-z0-9-]{0,31}$"))
        {
            return false;
        }

        var safeId = id ?? string.Empty;
        var dir = Path.Combine(_paths.Extend, safeId);
        if (!Directory.Exists(dir))
        {
            return false;
        }

        var full = Path.GetFullPath(dir);
        if (!full.StartsWith(_paths.Extend + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Directory.Delete(full, recursive: true);
        _logger.LogInformation("挂载已删除：{Id}", id);
        return true;
    }

    public bool SetEnabled(string id, bool enabled)
    {
        var manifestPath = Path.Combine(_paths.Extend, id, "extend.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = doc.RootElement;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.NameEquals("enabled"))
                {
                    writer.WriteBoolean("enabled", enabled);
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        File.WriteAllBytes(manifestPath, stream.ToArray());
        return true;
    }

    private static string NormalizeRoute(string? route)
    {
        var value = (route ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (!value.StartsWith('/'))
        {
            value = "/" + value;
        }

        return value.TrimEnd('/');
    }

    private static bool IsSafeUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url)
               && Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
