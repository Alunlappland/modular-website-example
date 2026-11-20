using Dapper;
using Genkan.Data;

namespace Genkan.Files;

public sealed class FileRow
{
    public string Id { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RelPath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Sha256 { get; set; }
    public string? Mime { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class ShareRow
{
    public string Id { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Kind { get; set; } = "share";
    public string? FileId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? ExpiresAt { get; set; }
    public int? MaxDownloads { get; set; }
    public int Downloads { get; set; }
    public string? Dir { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class FileRepository
{
    private readonly GenkanDb _db;

    public FileRepository(GenkanDb db) => _db = db;

    // ---------- 文件 ----------
    public void AddFile(FileRow file)
    {
        using var connection = _db.Open();
        connection.Execute(
            """
            INSERT INTO files(id, owner, name, rel_path, size, sha256, mime, created_at)
            VALUES (@Id, @Owner, @Name, @RelPath, @Size, @Sha256, @Mime, @CreatedAt)
            """,
            file);
    }

    public IReadOnlyList<FileRow> ListFiles(string owner)
    {
        using var connection = _db.Open();
        return connection.Query<FileRow>(
            "SELECT * FROM files WHERE owner = @owner ORDER BY created_at DESC",
            new { owner }).ToList();
    }

    public FileRow? GetFile(string id)
    {
        using var connection = _db.Open();
        return connection.QuerySingleOrDefault<FileRow>(
            "SELECT * FROM files WHERE id = @id", new { id });
    }

    public bool DeleteFile(string id, string owner)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "DELETE FROM files WHERE id = @id AND owner = @owner",
            new { id, owner }) > 0;
    }

    public void DeleteSharesForFile(string fileId, string owner)
    {
        using var connection = _db.Open();
        connection.Execute(
            "DELETE FROM shares WHERE file_id = @fileId AND owner = @owner",
            new { fileId, owner });
    }

    // ---------- 分享 ----------
    public void AddShare(ShareRow share)
    {
        using var connection = _db.Open();
        connection.Execute(
            """
            INSERT INTO shares(id, owner, kind, file_id, token, password_hash, expires_at, max_downloads, downloads, dir, created_at)
            VALUES (@Id, @Owner, @Kind, @FileId, @Token, @PasswordHash, @ExpiresAt, @MaxDownloads, @Downloads, @Dir, @CreatedAt)
            """,
            share);
    }

    public ShareRow? GetShare(string token)
    {
        using var connection = _db.Open();
        return connection.QuerySingleOrDefault<ShareRow>(
            "SELECT * FROM shares WHERE token = @token", new { token });
    }

    public IReadOnlyList<ShareRow> ListShares(string owner)
    {
        using var connection = _db.Open();
        return connection.Query<ShareRow>(
            "SELECT * FROM shares WHERE owner = @owner ORDER BY created_at DESC",
            new { owner }).ToList();
    }

    public bool DeleteShare(string id, string owner)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "DELETE FROM shares WHERE id = @id AND owner = @owner",
            new { id, owner }) > 0;
    }

    public void IncreaseDownload(string id)
    {
        using var connection = _db.Open();
        connection.Execute("UPDATE shares SET downloads = downloads + 1 WHERE id = @id", new { id });
    }
}
