using System.Text.Json;
using System.Text.Json.Serialization;

namespace Genkan.Ai;

public sealed class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public string At { get; set; } = DateTimeOffset.UtcNow.ToString("O");
}

public sealed class ChatDocument
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = "新对话";
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public List<ChatMessage> Messages { get; set; } = new();
}

public sealed record ChatSummary(string Id, string Title, string Provider, string Model, string UpdatedAt, int Messages);

public sealed class ChatStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly GenkanPaths _paths;
    private readonly ILogger<ChatStore> _logger;

    public ChatStore(GenkanPaths paths, ILogger<ChatStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    private string OwnerDirectory(string owner)
    {
        var safe = new string(owner.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        if (safe.Length == 0)
        {
            safe = "unknown";
        }

        return Path.Combine(_paths.Data, "chats", safe);
    }

    private string PathFor(string owner, string id)
    {
        var safeId = new string(id.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_').ToArray());
        return Path.Combine(OwnerDirectory(owner), safeId + ".json");
    }

    public IReadOnlyList<ChatSummary> List(string owner)
    {
        var directory = OwnerDirectory(owner);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<ChatSummary>();
        }

        var items = new List<ChatSummary>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var document = JsonSerializer.Deserialize<ChatDocument>(File.ReadAllText(file), Options);
                if (document is null)
                {
                    continue;
                }

                items.Add(new ChatSummary(
                    document.Id,
                    document.Title,
                    document.Provider,
                    document.Model,
                    document.UpdatedAt,
                    document.Messages.Count));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取对话文件失败：{File}", file);
            }
        }

        return items
            .OrderByDescending(item => item.UpdatedAt, StringComparer.Ordinal)
            .ToList();
    }

    public ChatDocument? Load(string owner, string id)
    {
        var path = PathFor(owner, id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChatDocument>(File.ReadAllText(path), Options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析对话失败：{Path}", path);
            return null;
        }
    }

    public void Save(string owner, ChatDocument document)
    {
        var directory = OwnerDirectory(owner);
        Directory.CreateDirectory(directory);
        document.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");

        var path = PathFor(owner, document.Id);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, Options));
        File.Move(temp, path, overwrite: true);
    }

    public bool Delete(string owner, string id)
    {
        var path = PathFor(owner, id);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }
}
