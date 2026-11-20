using System.Text.Json;

namespace Genkan;

/// <summary>一个挂载物（Extend 目录下的一个子目录）。</summary>
public sealed record ExtendEntry(
    string Id,
    string Title,
    string Route,
    string Kind,
    string Entry,
    string? Permission,
    bool Enabled,
    string Directory,
    int Order,
    string? Url);

/// <summary>
/// 扫描 <c>Extend\*\extend.json</c>，把磁盘上的挂载清单读进内存。
/// 磁盘是唯一事实来源；管理页保存后调用 <see cref="Reload"/> 即时生效。
/// </summary>
public sealed class ExtendRegistry
{
    private readonly object _gate = new();
    private readonly ILogger<ExtendRegistry> _logger;
    private IReadOnlyList<ExtendEntry> _entries = Array.Empty<ExtendEntry>();

    public ExtendRegistry(GenkanPaths paths, ILogger<ExtendRegistry> logger)
    {
        _logger = logger;
        Reload(paths);
    }

    public IReadOnlyList<ExtendEntry> Entries => _entries;

    public ExtendEntry? FindById(string id)
        => _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Reload(GenkanPaths paths)
    {
        var scanned = Scan(paths);
        lock (_gate)
        {
            _entries = scanned;
        }
    }

    private IReadOnlyList<ExtendEntry> Scan(GenkanPaths paths)
    {
        var list = new List<ExtendEntry>();

        if (!Directory.Exists(paths.Extend))
        {
            _logger.LogInformation("Extend 目录不存在，跳过挂载扫描：{Path}", paths.Extend);
            return list;
        }

        foreach (var dir in Directory.EnumerateDirectories(paths.Extend))
        {
            var manifest = Path.Combine(dir, "extend.json");
            if (!File.Exists(manifest))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = doc.RootElement;

                string Str(string name, string fallback = "")
                    => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() ?? fallback
                        : fallback;

                bool Bool(string name, bool fallback)
                    => root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? v.GetBoolean()
                        : fallback;

                int Int(string name, int fallback)
                    => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;

                var id = Str("id", Path.GetFileName(dir));
                var route = Str("route", "/" + id).Trim();
                if (!route.StartsWith('/'))
                {
                    route = "/" + route;
                }

                route = route.TrimEnd('/');
                if (route.Length == 0)
                {
                    route = "/" + id;
                }

                var permission = Str("permission");
                var kind = Str("kind", "static");

                list.Add(new ExtendEntry(
                    Id: id,
                    Title: Str("title", id),
                    Route: route,
                    Kind: kind,
                    Entry: Str("entry", "index.html"),
                    Permission: string.IsNullOrWhiteSpace(permission) ? null : permission,
                    Enabled: Bool("enabled", true),
                    Directory: dir,
                    Order: Int("order", 100),
                    Url: string.IsNullOrWhiteSpace(Str("url")) ? null : Str("url")));

                _logger.LogInformation("发现挂载物 {Id} → {Route}（{Kind}）", id, route, kind);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "挂载清单解析失败，已跳过：{Manifest}", manifest);
            }
        }

        return list.OrderBy(e => e.Order).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }
}
