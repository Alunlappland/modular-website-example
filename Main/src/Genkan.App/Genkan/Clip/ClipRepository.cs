using System.Text;
using Dapper;
using Genkan.Data;
using Microsoft.AspNetCore.DataProtection;

namespace Genkan.Clip;

public class ClipRow
{
    public string Id { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public bool Pinned { get; set; }
}

/// <summary>
/// 跨设备的文字剪贴板：
/// - 落库前用 DataProtection 加密（数据库文件被拷走也读不出内容）
/// - 超过保留天数自动过期（config: clip:retainDays，默认 30 天）
/// </summary>
public sealed class ClipRepository
{
    private const string Prefix = "v1:";

    private readonly GenkanDb _db;
    private readonly IDataProtector _protector;
    private readonly int _retainDays;

    public ClipRepository(GenkanDb db, IDataProtectionProvider provider, IConfiguration config)
    {
        _db = db;
        _protector = provider.CreateProtector("genkan.clip.v1");
        _retainDays = Math.Clamp(config.GetValue("clip:retainDays", 30), 1, 3650);
        MigrateLegacyRows();
    }

    public int RetainDays => _retainDays;

    private string Encrypt(string text) => Prefix + _protector.Protect(text);

    private string Decrypt(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return stored; // 老数据是明文
        }

        try
        {
            return _protector.Unprotect(stored[Prefix.Length..]);
        }
        catch (Exception)
        {
            return "（无法解密，可能是密钥环变过）";
        }
    }

    private void MigrateLegacyRows()
    {
        try
        {
            using var connection = _db.Open();
            var rows = connection.Query<LegacyClip>("SELECT id, text FROM clips").ToList();
            var stale = rows.Where(row => !row.Text.StartsWith(Prefix, StringComparison.Ordinal)).ToList();

            foreach (var row in stale)
            {
                connection.Execute(
                    "UPDATE clips SET text = @text WHERE id = @id",
                    new { id = row.Id, text = Encrypt(row.Text) });
            }
        }
        catch (Exception)
        {
            // 迁移失败不影响使用，下次启动再试
        }
    }


    private sealed class LegacyClip
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public IReadOnlyList<ClipRow> List(string owner, int limit = 80)
    {
        using var connection = _db.Open();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retainDays).ToString("O");
        connection.Execute("DELETE FROM clips WHERE owner = @owner AND pinned = 0 AND created_at < @cutoff", new { owner, cutoff });

        var rows = connection.Query<ClipRow>(
            "SELECT id, owner, text, created_at, pinned FROM clips WHERE owner = @owner ORDER BY pinned DESC, created_at DESC LIMIT @limit",
            new { owner, limit }).ToList();

        foreach (var row in rows)
        {
            row.Text = Decrypt(row.Text);
        }

        return rows;
    }

    public void Add(string owner, string text, bool pinned = false)
    {
        using var connection = _db.Open();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retainDays).ToString("O");
        connection.Execute("DELETE FROM clips WHERE owner = @owner AND pinned = 0 AND created_at < @cutoff", new { owner, cutoff });

        connection.Execute(
            "INSERT INTO clips(id, owner, text, created_at, pinned) VALUES (@id, @owner, @text, @createdAt, @pinned)",
            new
            {
                id = Guid.NewGuid().ToString("N"),
                owner,
                text = Encrypt(text),
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
                pinned = pinned ? 1 : 0,
            });
    }

    public bool SetPinned(string owner, string id, bool pinned)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "UPDATE clips SET pinned = @pinned WHERE id = @id AND owner = @owner",
            new { id, owner, pinned = pinned ? 1 : 0 }) > 0;
    }

    public bool Delete(string owner, string id)
    {
        using var connection = _db.Open();
        return connection.Execute(
            "DELETE FROM clips WHERE id = @id AND owner = @owner",
            new { id, owner }) > 0;
    }

    public int Clear(string owner)
    {
        using var connection = _db.Open();
        return connection.Execute("DELETE FROM clips WHERE owner = @owner", new { owner });
    }
}
