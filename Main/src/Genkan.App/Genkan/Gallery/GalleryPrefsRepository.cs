using Dapper;
using Genkan.Data;

namespace Genkan.Gallery;

public sealed class FavoriteRow
{
    public string Id { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string ImageKey { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string OriginalUrl { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class BlockedTagRow
{
    public string Tag { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class TagHistoryRow
{
    public string Query { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>图墙的收藏、屏蔽标签、搜索历史。</summary>
public sealed class GalleryPrefsRepository
{
    private readonly GenkanDb _db;

    public GalleryPrefsRepository(GenkanDb db) => _db = db;

    public IReadOnlyList<FavoriteRow> Favorites(string owner)
    {
        using var connection = _db.Open();
        return connection.Query<FavoriteRow>(
            "SELECT * FROM gallery_favorites WHERE owner = @owner ORDER BY created_at DESC",
            new { owner }).ToList();
    }

    public bool AddFavorite(string owner, FavoriteRow row)
    {
        using var connection = _db.Open();
        return connection.Execute(
            """
            INSERT INTO gallery_favorites(id, owner, image_key, preview_url, original_url, source, tags, created_at)
            VALUES (@id, @owner, @imageKey, @previewUrl, @originalUrl, @source, @tags, @createdAt)
            ON CONFLICT(owner, image_key) DO NOTHING
            """,
            new
            {
                id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id,
                owner,
                imageKey = row.ImageKey,
                previewUrl = row.PreviewUrl,
                originalUrl = row.OriginalUrl,
                source = row.Source,
                tags = row.Tags,
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
            }) > 0;
    }

    public bool RemoveFavorite(string owner, string imageKey)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "DELETE FROM gallery_favorites WHERE owner = @owner AND image_key = @imageKey",
            new { owner, imageKey }) > 0;
    }

    public IReadOnlyList<BlockedTagRow> BlockedTags(string owner)
    {
        using var connection = _db.Open();
        return connection.Query<BlockedTagRow>(
            "SELECT tag, created_at FROM gallery_blocked_tags WHERE owner = @owner ORDER BY created_at DESC",
            new { owner }).ToList();
    }

    public void BlockTag(string owner, string tag)
    {
        using var connection = _db.Open();
        connection.Execute(
            """
            INSERT INTO gallery_blocked_tags(id, owner, tag, created_at)
            VALUES (@id, @owner, @tag, @createdAt)
            ON CONFLICT(owner, tag) DO NOTHING
            """,
            new { id = Guid.NewGuid().ToString("N"), owner, tag, createdAt = DateTimeOffset.UtcNow.ToString("O") });
    }

    public bool UnblockTag(string owner, string tag)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "DELETE FROM gallery_blocked_tags WHERE owner = @owner AND tag = @tag",
            new { owner, tag }) > 0;
    }

    public IReadOnlyList<TagHistoryRow> History(string owner, int limit = 10)
    {
        using var connection = _db.Open();
        return connection.Query<TagHistoryRow>(
            "SELECT query, source, created_at FROM gallery_tag_history WHERE owner = @owner ORDER BY created_at DESC LIMIT @limit",
            new { owner, limit }).ToList();
    }

    public void RecordHistory(string owner, string query, string source)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        using var connection = _db.Open();
        connection.Execute(
            "INSERT INTO gallery_tag_history(id, owner, query, source, created_at) VALUES (@id, @owner, @query, @source, @createdAt)",
            new
            {
                id = Guid.NewGuid().ToString("N"),
                owner,
                query = query.Trim(),
                source,
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
            });

        // 只留最近 200 条
        connection.Execute(
            """
            DELETE FROM gallery_tag_history
            WHERE owner = @owner AND id NOT IN (
                SELECT id FROM gallery_tag_history WHERE owner = @owner ORDER BY created_at DESC LIMIT 200
            )
            """,
            new { owner });
    }
}
