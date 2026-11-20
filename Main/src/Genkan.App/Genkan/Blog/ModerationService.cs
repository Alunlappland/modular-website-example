using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Genkan.Blog;

/// <summary>
/// 评论审核。默认关闭（全部进人工队列）；开启后调用 Qwen（DashScope 兼容 OpenAI 接口），
/// 返回 allow / review / block。任何异常都降级为人工，不放行。
/// </summary>
public sealed class ModerationService
{
    private const string SystemPrompt =
        "你是中文博客的评论审核员。只输出一行 JSON：{\"verdict\":\"allow|review|block\",\"reason\":\"简短原因\"}。" +
        "含辱骂、人身攻击、广告引流、色情、违法或明显跑题的判 block；拿不准的判 review；正常交流判 allow。";

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ModerationService> _logger;

    public ModerationService(IHttpClientFactory http, IConfiguration config, ILogger<ModerationService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public bool Enabled => _config.GetValue("moderation:qwen:enabled", false);

    public async Task<string> ReviewAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!Enabled)
        {
            return "pending";
        }

        var apiKey = _config["moderation:qwen:apiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("审核已开启但没有配置 API Key，转人工。");
            return "pending";
        }

        var baseUrl = (_config["moderation:qwen:baseUrl"] ?? "https://dashscope.aliyuncs.com/compatible-mode/v1").TrimEnd('/');
        var model = _config["moderation:qwen:model"] ?? "qwen-turbo";

        try
        {
            var client = _http.CreateClient("moderation");
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var payload = new
            {
                model,
                temperature = 0,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = text },
                },
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("审核接口返回 {Status}，转人工。", (int)response.StatusCode);
                return "pending";
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var content = ExtractContent(json);
            var verdict = ExtractVerdict(content);

            return verdict switch
            {
                "allow" => "approved",
                "block" => "rejected",
                _ => "pending",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审核调用失败，转人工。");
            return "pending";
        }
    }

    private static string ExtractContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var message = choices[0].GetProperty("message");
                return message.GetProperty("content").GetString() ?? string.Empty;
            }
        }
        catch
        {
            // 忽略，按空内容处理
        }

        return string.Empty;
    }

    private static string ExtractVerdict(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return "review";
        }

        try
        {
            using var doc = JsonDocument.Parse(content[start..(end + 1)]);
            var verdict = doc.RootElement.TryGetProperty("verdict", out var value)
                ? value.GetString()
                : null;

            return (verdict ?? "review").Trim().ToLowerInvariant();
        }
        catch
        {
            return "review";
        }
    }
}
