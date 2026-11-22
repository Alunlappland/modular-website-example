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
    public string? CollectionId { get; set; }
    public string? LocalPath { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class CollectionRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public int Count { get; set; }
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

    public IReadOnlyList<FavoriteRow> Favorites(string owner, string? collectionId = null)
    {
        using var connection = _db.Open();

        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return connection.Query<FavoriteRow>(
                "SELECT * FROM gallery_favorites WHERE owner = @owner ORDER BY created_at DESC",
                new { owner }).ToList();
        }

        if (collectionId == "none")
        {
            return connection.Query<FavoriteRow>(
                "SELECT * FROM gallery_favorites WHERE owner = @owner AND collection_id IS NULL ORDER BY created_at DESC",
                new { owner }).ToList();
        }

        return connection.Query<FavoriteRow>(
            "SELECT * FROM gallery_favorites WHERE owner = @owner AND collection_id = @collectionId ORDER BY created_at DESC",
            new { owner, collectionId }).ToList();
    }

    public FavoriteRow? FindFavorite(string owner, string imageKey)
    {
        using var connection = _db.Open();
        return connection.QuerySingleOrDefault<FavoriteRow>(
            "SELECT * FROM gallery_favorites WHERE owner = @owner AND image_key = @imageKey",
            new { owner, imageKey });
    }

    public IReadOnlyList<CollectionRow> Collections(string owner)
    {
        using var connection = _db.Open();
        return connection.Query<CollectionRow>(
            """
            SELECT c.id, c.name, c.created_at,
                   (SELECT COUNT(*) FROM gallery_favorites f WHERE f.owner = c.owner AND f.collection_id = c.id) AS count
            FROM gallery_collections c
            WHERE c.owner = @owner
            ORDER BY c.created_at
            """,
            new { owner }).ToList();
    }

    public string CreateCollection(string owner, string name)
    {
        var id = Guid.NewGuid().ToString("N");
        using var connection = _db.Open();
        connection.Execute(
            "INSERT INTO gallery_collections(id, owner, name, created_at) VALUES (@id, @owner, @name, @createdAt)",
            new { id, owner, name, createdAt = DateTimeOffset.UtcNow.ToString("O") });

        return id;
    }

    public bool DeleteCollection(string owner, string id)
    {
        using var connection = _db.Open();
        connection.Execute(
            "UPDATE gallery_favorites SET collection_id = NULL WHERE owner = @owner AND collection_id = @id",
            new { owner, id });

        return connection.Execute(
            "DELETE FROM gallery_collections WHERE owner = @owner AND id = @id",
            new { owner, id }) > 0;
    }

    public bool AddFavorite(string owner, FavoriteRow row)
    {
        using var connection = _db.Open();
        return connection.Execute(
            """
            INSERT INTO gallery_favorites(id, owner, image_key, preview_url, original_url, source, tags, collection_id, local_path, created_at)
            VALUES (@id, @owner, @imageKey, @previewUrl, @originalUrl, @source, @tags, @collectionId, @localPath, @createdAt)
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
                collectionId = string.IsNullOrWhiteSpace(row.CollectionId) ? null : row.CollectionId,
                localPath = string.IsNullOrWhiteSpace(row.LocalPath) ? null : row.LocalPath,
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
            }) > 0;
    }

    public void SetLocalPath(string owner, string imageKey, string localPath)
    {
        using var connection = _db.Open();
        connection.Execute(
            "UPDATE gallery_favorites SET local_path = @localPath WHERE owner = @owner AND image_key = @imageKey",
            new { owner, imageKey, localPath });
    }

    public void UpdateCollection(string owner, string imageKey, string? collectionId)
    {
        using var connection = _db.Open();
        connection.Execute(
            "UPDATE gallery_favorites SET collection_id = @collectionId WHERE owner = @owner AND image_key = @imageKey",
            new { owner, imageKey, collectionId = string.IsNullOrWhiteSpace(collectionId) ? null : collectionId });
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
            """
            SELECT query, MAX(source) AS source, MAX(created_at) AS created_at
            FROM gallery_tag_history
            WHERE owner = @owner
            GROUP BY query
            ORDER BY created_at DESC
            LIMIT @limit
            """,
            new { owner, limit }).ToList();
    }

    public void RecordHistory(string owner, string query, string source)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        using var connection = _db.Open();

        // 同一个标签只留一条，重新搜就置顶
        connection.Execute(
            "DELETE FROM gallery_tag_history WHERE owner = @owner AND query = @query",
            new { owner, query = query.Trim() });

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
