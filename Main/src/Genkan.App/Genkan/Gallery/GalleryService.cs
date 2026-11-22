using System.Collections.Concurrent;
using System.Text.Json;

namespace Genkan.Gallery;

public sealed record GalleryImage(
    string Id,
    string PreviewUrl,
    string OriginalUrl,
    int Width,
    int Height,
    string Tags,
    string Source);

/// <summary>
/// 图源代理：只接 SFW 的两个源（konachan.net 的 safe 区、safebooru），
/// 结果在内存里缓存几分钟，图片也可以经服务器转发（避免热链接与 CORS）。
/// </summary>
public sealed class GalleryService
{
    private static readonly string[] AllowedHosts =
    {
        "konachan.net", "konachan.com", "safebooru.org", "img.safebooru.org",
    };

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GalleryService> _logger;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<GalleryImage> Items)> _cache = new();

    public GalleryService(IHttpClientFactory http, IConfiguration config, ILogger<GalleryService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public string Provider => (_config["gallery:provider"] ?? "konachan").Trim().ToLowerInvariant();

    public string DefaultTags => (_config["gallery:tags"] ?? "safe").Trim();

    public int PageSize => Math.Clamp(_config.GetValue("gallery:limit", 24), 4, 48);

    public async Task<IReadOnlyList<GalleryImage>> FetchAsync(int page, string? tags, CancellationToken cancellationToken)
    {
        page = Math.Clamp(page, 1, 200);
        var tagQuery = string.IsNullOrWhiteSpace(tags) ? DefaultTags : tags.Trim();

        var cacheKey = Provider + "|" + tagQuery + "|" + page;
        if (_cache.TryGetValue(cacheKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(5))
        {
            return cached.Items;
        }

        var items = Provider == "safebooru"
            ? await FetchSafebooruAsync(page, tagQuery, cancellationToken)
            : await FetchKonachanAsync(page, tagQuery, cancellationToken);

        _cache[cacheKey] = (DateTimeOffset.UtcNow, items);

        // 缓存别无限长
        if (_cache.Count > 300)
        {
            foreach (var key in _cache.Keys.Take(100))
            {
                _cache.TryRemove(key, out _);
            }
        }

        return items;
    }

    private async Task<IReadOnlyList<GalleryImage>> FetchKonachanAsync(int page, string tags, CancellationToken cancellationToken)
    {
        var baseUrl = _config["gallery:baseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://konachan.net";
        }

        var url = $"{baseUrl.TrimEnd('/')}/post.json?limit={PageSize}&page={page}&tags={Uri.EscapeDataString(tags)}";
        var client = _http.CreateClient("gallery");

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("图源返回 {Status}", (int)response.StatusCode);
                return Array.Empty<GalleryImage>();
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var list = new List<GalleryImage>();

            foreach (var node in document.RootElement.EnumerateArray())
            {
                string Str(string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";

                var id = node.TryGetProperty("id", out var idNode) ? idNode.GetRawText() : Str("id");
                var preview = Str("sample_url");
                var original = Str("file_url");
                if (string.IsNullOrEmpty(preview)) preview = Str("jpeg_url");
                if (string.IsNullOrEmpty(original)) original = Str("jpeg_url");
                if (string.IsNullOrEmpty(preview) || string.IsNullOrEmpty(original)) continue;

                list.Add(new GalleryImage(
                    id,
                    preview,
                    original,
                    node.TryGetProperty("width", out var w) && w.TryGetInt32(out var width) ? width : 0,
                    node.TryGetProperty("height", out var h) && h.TryGetInt32(out var height) ? height : 0,
                    Str("tags"),
                    "konachan"));
            }

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取 konachan 失败");
            return Array.Empty<GalleryImage>();
        }
    }

    private async Task<IReadOnlyList<GalleryImage>> FetchSafebooruAsync(int page, string tags, CancellationToken cancellationToken)
    {
        var baseUrl = _config["gallery:baseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://safebooru.org";
        }

        var pid = Math.Max(0, page - 1);
        var url = $"{baseUrl.TrimEnd('/')}/index.php?page=dapi&s=post&q=index&json=1&limit={PageSize}&pid={pid}&tags={Uri.EscapeDataString(tags)}";
        var client = _http.CreateClient("gallery");

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<GalleryImage>();
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var list = new List<GalleryImage>();

            foreach (var node in document.RootElement.EnumerateArray())
            {
                string Str(string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? ""
                    : "";

                var directory = Str("directory");
                var image = Str("image");
                if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(image)) continue;

                var original = $"https://safebooru.org/images/{directory}/{image}";
                var preview = $"https://safebooru.org/samples/{directory}/sample_{image}";
                if (preview.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) ||
                    preview.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                list.Add(new GalleryImage(
                    node.TryGetProperty("id", out var idNode) ? idNode.GetRawText() : image,
                    preview,
                    original,
                    node.TryGetProperty("width", out var w) && w.TryGetInt32(out var width) ? width : 0,
                    node.TryGetProperty("height", out var h) && h.TryGetInt32(out var height) ? height : 0,
                    Str("tags"),
                    "safebooru"));
            }

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "拉取 safebooru 失败");
            return Array.Empty<GalleryImage>();
        }
    }

    /// <summary>只转发白名单主机的图片，防止被当成任意代理。</summary>
    public static bool IsAllowedImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return AllowedHosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(byte[] Bytes, string ContentType)?> ProxyImageAsync(string url, CancellationToken cancellationToken)
    {
        if (!IsAllowedImageUrl(url))
        {
            return null;
        }

        var client = _http.CreateClient("gallery");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > 12 * 1024 * 1024)
        {
            return null;
        }

        return (buffer.ToArray(), contentType);
    }
}
