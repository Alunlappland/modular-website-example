using Dapper;
using Genkan.Data;

namespace Genkan.Blog;

// Dapper 需要无参构造 + 可写属性（record 的构造参数名不参与 snake_case 映射）
public sealed class CommentRow
{
    public long Id { get; set; }
    public string PostSlug { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class AnnotationRow
{
    public long Id { get; set; }
    public string PostSlug { get; set; } = string.Empty;
    public int BlockIndex { get; set; }
    public string BlockHash { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed record ReactionCounts(int Likes, int Dislikes);

/// <summary>评论 / 行级注释 / 点赞点踩的读写。</summary>
public sealed class BlogRepository
{
    private readonly GenkanDb _db;

    public BlogRepository(GenkanDb db) => _db = db;

    // ---------- 评论 ----------
    public long AddComment(string slug, string author, string body, string status)
    {
        using var connection = _db.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO comments(post_slug, author, body, status, created_at)
            VALUES (@slug, @author, @body, @status, @at);
            SELECT last_insert_rowid();
            """,
            new { slug, author, body, status, at = DateTimeOffset.UtcNow.ToString("O") });
    }

    public IReadOnlyList<CommentRow> Comments(string slug, bool includePending)
    {
        using var connection = _db.Open();
        var sql = includePending
            ? "SELECT * FROM comments WHERE post_slug = @slug ORDER BY created_at"
            : "SELECT * FROM comments WHERE post_slug = @slug AND status = 'approved' ORDER BY created_at";

        return connection.Query<CommentRow>(sql, new { slug }).ToList();
    }

    public bool SetCommentStatus(long id, string status)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "UPDATE comments SET status = @status WHERE id = @id",
            new { id, status }) > 0;
    }

    public bool DeleteComment(long id)
    {
        using var connection = _db.Open();
        return connection.Execute("DELETE FROM comments WHERE id = @id", new { id }) > 0;
    }

    // ---------- 行级注释 ----------
    public long AddAnnotation(string slug, int blockIndex, string blockHash, string author, string body, string status)
    {
        using var connection = _db.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO annotations(post_slug, block_index, block_hash, author, body, status, created_at)
            VALUES (@slug, @blockIndex, @blockHash, @author, @body, @status, @at);
            SELECT last_insert_rowid();
            """,
            new { slug, blockIndex, blockHash, author, body, status, at = DateTimeOffset.UtcNow.ToString("O") });
    }

    public IReadOnlyList<AnnotationRow> Annotations(string slug, bool includePending)
    {
        using var connection = _db.Open();
        var sql = includePending
            ? "SELECT * FROM annotations WHERE post_slug = @slug ORDER BY block_index, created_at"
            : "SELECT * FROM annotations WHERE post_slug = @slug AND status = 'approved' ORDER BY block_index, created_at";

        return connection.Query<AnnotationRow>(sql, new { slug }).ToList();
    }

    public bool SetAnnotationStatus(long id, string status)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "UPDATE annotations SET status = @status WHERE id = @id",
            new { id, status }) > 0;
    }

    public bool DeleteAnnotation(long id)
    {
        using var connection = _db.Open();
        return connection.Execute("DELETE FROM annotations WHERE id = @id", new { id }) > 0;
    }

    /// <summary>10 分钟内同一个人在同一块上提交同样内容，视为重复，直接返回已有记录。</summary>
    public long? FindRecentDuplicate(string slug, int blockIndex, string author, string body)
    {
        using var connection = _db.Open();
        return connection.QuerySingleOrDefault<long?>(
            """
            SELECT id FROM annotations
            WHERE post_slug = @slug AND block_index = @blockIndex
              AND author = @author AND body = @body AND created_at > @since
            LIMIT 1
            """,
            new
            {
                slug,
                blockIndex,
                author,
                body,
                since = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"),
            });
    }

    // ---------- 反应 ----------
    public void React(string slug, string kind, string fingerprint)
    {
        using var connection = _db.Open();
        connection.Execute(
            """
            INSERT OR IGNORE INTO reactions(post_slug, kind, fingerprint, created_at)
            VALUES (@slug, @kind, @fingerprint, @at)
            """,
            new { slug, kind, fingerprint, at = DateTimeOffset.UtcNow.ToString("O") });
    }

    public ReactionCounts Counts(string slug)
    {
        using var connection = _db.Open();
        var likes = connection.ExecuteScalar<int>(
            "SELECT count(*) FROM reactions WHERE post_slug = @slug AND kind = 'like'", new { slug });
        var dislikes = connection.ExecuteScalar<int>(
            "SELECT count(*) FROM reactions WHERE post_slug = @slug AND kind = 'dislike'", new { slug });

        return new ReactionCounts(likes, dislikes);
    }
}
