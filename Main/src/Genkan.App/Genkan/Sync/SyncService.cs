using System.Text.Json;

namespace Genkan.Sync;

public sealed record SyncthingStatus(string Version, string DeviceName, string MyId, long UptimeSeconds);

public sealed record SyncthingFolder(string Id, string Label, string Path, string Type, bool Paused);

public sealed record SyncthingDevice(string DeviceId, string Name, bool Paused);

/// <summary>
/// Syncthing REST API 的只读封装（状态 / 文件夹 / 设备）。
/// API Key 在 Syncthing 的 config.xml 里，填到 appsettings 的 sync:syncthing:apiKey。
/// </summary>
public sealed class SyncService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<SyncService> _logger;

    public SyncService(IConfiguration config, IHttpClientFactory http, ILogger<SyncService> logger)
    {
        _config = config;
        _http = http;
        _logger = logger;
    }

    public string Url => (_config["sync:syncthing:url"] ?? "http://127.0.0.1:8384").TrimEnd('/');

    public string ApiKey => _config["sync:syncthing:apiKey"] ?? string.Empty;

    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<SyncthingStatus?> GetStatusAsync(CancellationToken cancellationToken)
    {
        var json = await GetJsonAsync("/rest/system/status", cancellationToken);
        if (json is null)
        {
            return null;
        }

        var version = json.Value.TryGetProperty("version", out var versionValue) ? versionValue.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(version))
        {
            var versionJson = await GetJsonAsync("/rest/system/version", cancellationToken);
            if (versionJson is not null && versionJson.Value.TryGetProperty("version", out var v2))
            {
                version = v2.GetString() ?? "";
            }
        }

        return new SyncthingStatus(
            version,
            json.Value.TryGetProperty("deviceName", out var name) ? name.GetString() ?? "" : "",
            json.Value.TryGetProperty("myID", out var id) ? id.GetString() ?? "" : "",
            json.Value.TryGetProperty("uptime", out var uptime) && uptime.TryGetInt64(out var seconds) ? seconds : 0);
    }

    public async Task<IReadOnlyList<SyncthingFolder>> GetFoldersAsync(CancellationToken cancellationToken)
    {
        var json = await GetJsonAsync("/rest/config/folders", cancellationToken);
        if (json is null || json.Value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SyncthingFolder>();
        }

        var list = new List<SyncthingFolder>();
        foreach (var item in json.Value.EnumerateArray())
        {
            list.Add(new SyncthingFolder(
                item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                item.TryGetProperty("label", out var label) ? label.GetString() ?? "" : "",
                item.TryGetProperty("path", out var path) ? path.GetString() ?? "" : "",
                item.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                item.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True));
        }

        return list;
    }

    public async Task<IReadOnlyList<SyncthingDevice>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var json = await GetJsonAsync("/rest/config/devices", cancellationToken);
        if (json is null || json.Value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SyncthingDevice>();
        }

        var list = new List<SyncthingDevice>();
        foreach (var item in json.Value.EnumerateArray())
        {
            list.Add(new SyncthingDevice(
                item.TryGetProperty("deviceID", out var id) ? id.GetString() ?? "" : "",
                item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                item.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True));
        }

        return list;
    }

    private async Task<JsonElement?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        if (!Configured)
        {
            return null;
        }

        try
        {
            var client = _http.CreateClient("syncthing");
            using var request = new HttpRequestMessage(HttpMethod.Get, Url + path);
            request.Headers.TryAddWithoutValidation("X-API-Key", ApiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Syncthing {Path} 返回 {Status}", path, (int)response.StatusCode);
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Syncthing 不可达（{Url}）", Url);
            return null;
        }
    }
}
