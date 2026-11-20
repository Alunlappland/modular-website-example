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
    int Order);

/// <summary>
/// 扫描 <c>Extend\*\extend.json</c>，把磁盘上的挂载清单读进内存。
/// 磁盘是唯一事实来源，界面上的增删改最终都写回磁盘。
/// </summary>
public sealed class ExtendRegistry
{
    private readonly List<ExtendEntry> _entries = new();

    public IReadOnlyList<ExtendEntry> Entries => _entries;

    public ExtendRegistry(GenkanPaths paths, ILogger<ExtendRegistry> logger)
        => Scan(paths, logger);

    public void Scan(GenkanPaths paths, ILogger logger)
    {
        _entries.Clear();
        if (!Directory.Exists(paths.Extend))
        {
            logger.LogInformation("Extend 目录不存在，跳过挂载扫描：{Path}", paths.Extend);
            return;
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
                _entries.Add(new ExtendEntry(
                    Id: id,
                    Title: Str("title", id),
                    Route: route,
                    Kind: Str("kind", "static"),
                    Entry: Str("entry", "index.html"),
                    Permission: string.IsNullOrWhiteSpace(permission) ? null : permission,
                    Enabled: Bool("enabled", true),
                    Directory: dir,
                    Order: Int("order", 100)));

                logger.LogInformation("发现挂载物 {Id} → {Route}（{Kind}）", id, route, Str("kind", "static"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "挂载清单解析失败，已跳过：{Manifest}", manifest);
            }
        }

        _entries.Sort((a, b) => a.Order.CompareTo(b.Order));
    }
}
