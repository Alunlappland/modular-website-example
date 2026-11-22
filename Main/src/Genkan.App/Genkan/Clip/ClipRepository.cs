using Dapper;
using Genkan.Data;

namespace Genkan.Clip;

public class ClipRow
{
    public string Id { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>跨设备的文字剪贴板：整段文本存 SQLite，读出来点一下复制。</summary>
public sealed class ClipRepository
{
    private readonly GenkanDb _db;

    public ClipRepository(GenkanDb db) => _db = db;

    public IReadOnlyList<ClipRow> List(string owner, int limit = 80)
    {
        using var connection = _db.Open();
        return connection.Query<ClipRow>(
            "SELECT id, owner, text, created_at FROM clips WHERE owner = @owner ORDER BY created_at DESC LIMIT @limit",
            new { owner, limit }).ToList();
    }

    public void Add(string owner, string text)
    {
        using var connection = _db.Open();
        connection.Execute(
            "INSERT INTO clips(id, owner, text, created_at) VALUES (@id, @owner, @text, @createdAt)",
            new
            {
                id = Guid.NewGuid().ToString("N"),
                owner,
                text,
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
            });
    }

    public bool Delete(string owner, string id)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "DELETE FROM clips WHERE id = @id AND owner = @owner",
            new { id, owner }) > 0;
    }
}
