using System.Security.Cryptography;
using System.Text;
using Genkan.Security;

namespace Genkan.Files;

/// <summary>分享链接与收件箱的创建与校验。</summary>
public sealed class ShareService
{
    private readonly FileRepository _repository;
    private readonly FileStorage _storage;

    public ShareService(FileRepository repository, FileStorage storage)
    {
        _repository = repository;
        _storage = storage;
    }

    public ShareRow CreateFileShare(
        string owner,
        string fileId,
        int? validDays,
        int? maxDownloads,
        string? password)
    {
        var share = new ShareRow
        {
            Id = Guid.NewGuid().ToString("N"),
            Owner = owner,
            Kind = "share",
            FileId = fileId,
            Token = NewToken(),
            PasswordHash = EncodePassword(password),
            ExpiresAt = Expiry(validDays),
            MaxDownloads = maxDownloads is > 0 ? maxDownloads : null,
            Downloads = 0,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };

        _repository.AddShare(share);
        return share;
    }

    public ShareRow CreateDrop(string owner, int? validDays, string? password)
    {
        // 收件箱目录名 = sha256(随机盐 + 时间) 前 32 位，不可枚举
        var seed = Guid.NewGuid().ToString("N") + DateTimeOffset.UtcNow.ToString("O");
        var folder = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant()[..32];

        var share = new ShareRow
        {
            Id = Guid.NewGuid().ToString("N"),
            Owner = owner,
            Kind = "drop",
            Token = NewToken(),
            PasswordHash = EncodePassword(password),
            ExpiresAt = Expiry(validDays),
            MaxDownloads = null,
            Downloads = 0,
            Dir = folder,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };

        Directory.CreateDirectory(Path.Combine(_storage.InboxRoot, folder));
        _repository.AddShare(share);
        return share;
    }

    public ShareRow? Resolve(string token)
    {
        var share = _repository.GetShare(token);
        if (share is null)
        {
            return null;
        }

        if (IsExpired(share))
        {
            return null;
        }

        if (share.Kind == "share" && share.MaxDownloads is > 0 && share.Downloads >= share.MaxDownloads)
        {
            return null;
        }

        return share;
    }

    public bool CheckPassword(ShareRow share, string? password)
    {
        if (string.IsNullOrEmpty(share.PasswordHash))
        {
            return true;
        }

        var parts = share.PasswordHash.Split('|');
        if (parts.Length != 3 || !int.TryParse(parts[2], out var iterations))
        {
            return false;
        }

        return PasswordHasher.Verify(password ?? string.Empty, parts[0], parts[1], iterations);
    }

    public static bool IsExpired(ShareRow share)
        => !string.IsNullOrWhiteSpace(share.ExpiresAt)
           && DateTimeOffset.TryParse(share.ExpiresAt, out var expires)
           && expires < DateTimeOffset.UtcNow;

    public static string Describe(ShareRow share)
    {
        if (IsExpired(share))
        {
            return "已过期";
        }

        if (share.Kind == "share" && share.MaxDownloads is > 0 && share.Downloads >= share.MaxDownloads)
        {
            return "已达下载上限";
        }

        return string.IsNullOrWhiteSpace(share.ExpiresAt) ? "长期有效" : "有效至 " + share.ExpiresAt;
    }

    private static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string? EncodePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var (salt, hash, iterations) = PasswordHasher.Create(password);
        return $"{salt}|{hash}|{iterations}";
    }

    private static string? Expiry(int? days)
        => days is > 0 ? DateTimeOffset.UtcNow.AddDays(days.Value).ToString("O") : null;
}
