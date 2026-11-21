using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Genkan.Backup;

namespace Genkan.Ai;

public sealed record BalanceInfo(string ProviderId, string Name, bool Ok, string Amount, string Detail);

public sealed class AiService
{
    private readonly AiCatalog _catalog;
    private readonly SecretStore _secrets;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<AiService> _logger;

    public AiService(AiCatalog catalog, SecretStore secrets, IHttpClientFactory http, ILogger<AiService> logger)
    {
        _catalog = catalog;
        _secrets = secrets;
        _http = http;
        _logger = logger;
    }

    public AiCatalog Catalog => _catalog;

    public bool HasKey(string providerId) => _secrets.Has($"ai.{providerId}.key");

    public string? KeyOf(string providerId) => _secrets.Get($"ai.{providerId}.key");

    public void SaveKey(string providerId, string key) => _secrets.Set($"ai.{providerId}.key", key.Trim());

    public void ClearKey(string providerId) => _secrets.Remove($"ai.{providerId}.key");

    // ---------------- 对话（流式） ----------------

    public async Task StreamChatAsync(
        AiProvider provider,
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var key = KeyOf(provider.Id);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"{provider.Name} 还没有配置 API Key。");
        }

        var client = _http.CreateClient("ai");

        switch (provider.Kind.ToLowerInvariant())
        {
            case "anthropic":
                await StreamAnthropicAsync(client, provider, key, messages, onDelta, cancellationToken);
                break;
            case "gemini":
                await StreamGeminiAsync(client, provider, key, messages, onDelta, cancellationToken);
                break;
            default:
                await StreamOpenAiAsync(client, provider, key, messages, onDelta, cancellationToken);
                break;
        }
    }

    private async Task StreamOpenAiAsync(
        HttpClient client,
        AiProvider provider,
        string key,
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = provider.Model,
            stream = true,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, provider, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data == "[DONE]")
            {
                break;
            }

            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        await onDelta(text);
                    }
                }
            }
            catch (JsonException)
            {
                // 供应商偶尔会插入非 JSON 保活行，忽略
            }
        }
    }

    private async Task StreamAnthropicAsync(
        HttpClient client,
        AiProvider provider,
        string key,
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = provider.Model,
            max_tokens = 4096,
            stream = true,
            messages = messages
                .Where(message => message.Role is "user" or "assistant")
                .Select(message => new { role = message.Role, content = message.Content })
                .ToArray(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{provider.BaseUrl}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, provider, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.TryGetProperty("type", out var type) &&
                    type.GetString() == "content_block_delta" &&
                    document.RootElement.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    var value = text.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        await onDelta(value);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    private async Task StreamGeminiAsync(
        HttpClient client,
        AiProvider provider,
        string key,
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken)
    {
        var contents = messages
            .Where(message => message.Role is "user" or "assistant")
            .Select(message => new
            {
                role = message.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = message.Content } },
            })
            .ToArray();

        var url = $"{provider.BaseUrl}/models/{provider.Model}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(key)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { contents }), Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, provider, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(data);
                if (document.RootElement.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0 &&
                    candidates[0].TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    var value = text.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        await onDelta(value);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, AiProvider provider, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 300)
        {
            body = body[..300];
        }

        throw new InvalidOperationException($"{provider.Name} 返回 {(int)response.StatusCode}：{body}");
    }

    // ---------------- 余额 ----------------

    public async Task<IReadOnlyList<BalanceInfo>> BalancesAsync(CancellationToken cancellationToken)
    {
        var results = new List<BalanceInfo>();

        foreach (var provider in _catalog.Providers)
        {
            if (!HasKey(provider.Id))
            {
                results.Add(new BalanceInfo(provider.Id, provider.Name, false, "—", "未配置 Key"));
                continue;
            }

            if (provider.Balance == "none")
            {
                results.Add(new BalanceInfo(provider.Id, provider.Name, false, "—", "该供应商不提供余额接口"));
                continue;
            }

            try
            {
                results.Add(await BalanceAsync(provider, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "查询 {Provider} 余额失败", provider.Id);
                results.Add(new BalanceInfo(provider.Id, provider.Name, false, "—", ex.Message));
            }
        }

        return results;
    }

    private async Task<BalanceInfo> BalanceAsync(AiProvider provider, CancellationToken cancellationToken)
    {
        var key = KeyOf(provider.Id) ?? string.Empty;
        var client = _http.CreateClient("ai");

        switch (provider.Balance)
        {
            case "deepseek":
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var infos = document.RootElement.GetProperty("balance_infos");
                if (infos.GetArrayLength() == 0)
                {
                    return new BalanceInfo(provider.Id, provider.Name, true, "0.00", "无余额信息");
                }

                var first = infos[0];
                var total = first.GetProperty("total_balance").GetString();
                var currency = first.GetProperty("currency").GetString();
                return new BalanceInfo(provider.Id, provider.Name, true, $"{total} {currency}", "DeepSeek 账户余额");
            }

            case "moonshot":
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.moonshot.cn/v1/users/me/balance");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var data = document.RootElement.GetProperty("data");
                return new BalanceInfo(
                    provider.Id,
                    provider.Name,
                    true,
                    data.GetProperty("available_balance").GetRawText(),
                    "可用余额（元）");
            }

            case "siliconflow":
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.siliconflow.cn/v1/user/info");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var data = document.RootElement.GetProperty("data");
                return new BalanceInfo(
                    provider.Id,
                    provider.Name,
                    true,
                    data.GetProperty("totalBalance").GetRawText(),
                    "总余额");
            }

            case "openrouter":
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/credits");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var data = document.RootElement.GetProperty("data");
                var credits = data.GetProperty("total_credits").GetDouble();
                var usage = data.GetProperty("total_usage").GetDouble();
                return new BalanceInfo(
                    provider.Id,
                    provider.Name,
                    true,
                    (credits - usage).ToString("F2") + " USD",
                    $"已用 {usage:F2} / 总额 {credits:F2}");
            }

            default:
                return new BalanceInfo(provider.Id, provider.Name, false, "—", "未知余额类型");
        }
    }
}
