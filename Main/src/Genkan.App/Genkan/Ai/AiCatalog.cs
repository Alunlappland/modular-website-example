namespace Genkan.Ai;

/// <summary>配置里的一个 AI 供应商。密钥不在这里，存在 SecretStore（ai.&lt;id&gt;.key）。</summary>
public sealed class AiProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "openai";
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Balance { get; set; } = "none";
    public bool Enabled { get; set; } = true;
}

public sealed record AiProvider(
    string Id,
    string Name,
    string Kind,
    string BaseUrl,
    string Model,
    string Balance,
    bool Enabled);

public sealed class AiCatalog
{
    private readonly IConfiguration _config;

    public AiCatalog(IConfiguration config) => _config = config;

    public IReadOnlyList<AiProvider> Providers
        => _config.GetSection("ai:providers").Get<List<AiProviderConfig>>()?
               .Where(item => item.Enabled && !string.IsNullOrWhiteSpace(item.Id))
               .Select(item => new AiProvider(
                   item.Id,
                   string.IsNullOrWhiteSpace(item.Name) ? item.Id : item.Name,
                   item.Kind,
                   item.BaseUrl.TrimEnd('/'),
                   item.Model,
                   item.Balance,
                   item.Enabled))
               .ToList()
           ?? new List<AiProvider>();

    public string DefaultProvider => _config["ai:defaultProvider"] ?? Providers.FirstOrDefault()?.Id ?? "deepseek";

    public AiProvider? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            id = DefaultProvider;
        }

        return Providers.FirstOrDefault(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
