using System.Security.Cryptography;
using System.Text;
using Dapper;
using Genkan.Data;

namespace Genkan.Api;

public static class ApiScopes
{
    public const string BlogRead = "blog.read";
    public const string NotesRead = "notes.read";
    public const string FilesRead = "files.read";
    public const string ClipsRead = "clips.read";
    public const string ClipsWrite = "clips.write";
    public const string StatusRead = "status.read";

    public static readonly string[] All =
    {
        BlogRead, NotesRead, FilesRead, ClipsRead, ClipsWrite, StatusRead,
    };
}

public sealed class ApiKeyRow
{
    public string Id { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string Scopes { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? LastUsedAt { get; set; }
    public string? RevokedAt { get; set; }
}

/// <summary>
/// 第三方附属程序用的 API Key：只存哈希，明文仅在创建时显示一次。
/// Key 绑定到某个站内账号，能访问的数据范围不超过该账号。
/// </summary>
public sealed class ApiKeyRepository
{
    private readonly GenkanDb _db;

    public ApiKeyRepository(GenkanDb db) => _db = db;

    public static string Hash(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    public static (string Key, string Hash) CreateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var key = "gk_" + Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "");
        return (key, Hash(key));
    }

    public IReadOnlyList<ApiKeyRow> List(string? ownerId = null)
    {
        using var connection = _db.Open();
        return ownerId is null
            ? connection.Query<ApiKeyRow>("SELECT * FROM api_keys ORDER BY created_at DESC").ToList()
            : connection.Query<ApiKeyRow>("SELECT * FROM api_keys WHERE owner_id = @ownerId ORDER BY created_at DESC", new { ownerId }).ToList();
    }

    public string Create(string ownerId, string name, IEnumerable<string> scopes)
    {
        var (key, hash) = CreateKey();

        using var connection = _db.Open();
        connection.Execute(
            """
            INSERT INTO api_keys(id, owner_id, name, key_hash, scopes, created_at)
            VALUES (@id, @ownerId, @name, @hash, @scopes, @createdAt)
            """,
            new
            {
                id = Guid.NewGuid().ToString("N"),
                ownerId,
                name,
                hash,
                scopes = string.Join(',', scopes),
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
            });

        return key;
    }

    public ApiKeyRow? Authenticate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var hash = Hash(key);

        using var connection = _db.Open();
        var row = connection.QuerySingleOrDefault<ApiKeyRow>(
            "SELECT * FROM api_keys WHERE key_hash = @hash AND revoked_at IS NULL",
            new { hash });

        if (row is not null)
        {
            connection.Execute(
                "UPDATE api_keys SET last_used_at = @at WHERE id = @id",
                new { at = DateTimeOffset.UtcNow.ToString("O"), id = row.Id });
        }

        return row;
    }

    public bool Revoke(string id)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "UPDATE api_keys SET revoked_at = @at WHERE id = @id AND revoked_at IS NULL",
            new { at = DateTimeOffset.UtcNow.ToString("O"), id }) > 0;
    }

    public static bool HasScope(ApiKeyRow key, string scope)
        => key.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(scope, StringComparer.OrdinalIgnoreCase);
}
