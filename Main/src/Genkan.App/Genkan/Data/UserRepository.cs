using Dapper;

namespace Genkan.Data;

/// <summary>
/// 数据库行。必须是可赋值的类（Dapper 需要无参构造 + 可写属性；record 的构造参数名
/// 不会跟随 snake_case 映射）。
/// </summary>
public sealed class UserRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string Role { get; set; } = "member";
    public string PwdAlgo { get; set; } = "pbkdf2-sha256";
    public string PwdSalt { get; set; } = string.Empty;
    public string PwdHash { get; set; } = string.Empty;
    public int PwdIter { get; set; }
    public bool Enabled { get; set; } = true;
    public string CreatedAt { get; set; } = string.Empty;
    public string? LastLoginAt { get; set; }
}

public sealed class UserRepository
{
    private readonly GenkanDb _db;

    public UserRepository(GenkanDb db) => _db = db;

    public int Count()
    {
        using var connection = _db.Open();
        return connection.ExecuteScalar<int>("SELECT count(*) FROM users");
    }

    public UserRecord? FindByName(string name)
    {
        using var connection = _db.Open();
        return connection.QuerySingleOrDefault<UserRecord>(
            "SELECT * FROM users WHERE name = @name",
            new { name });
    }

    public UserRecord? FindById(string id)
    {
        using var connection = _db.Open();
        return connection.QuerySingleOrDefault<UserRecord>(
            "SELECT * FROM users WHERE id = @id",
            new { id });
    }

    public IReadOnlyList<UserRecord> List()
    {
        using var connection = _db.Open();
        return connection.Query<UserRecord>("SELECT * FROM users ORDER BY created_at").ToList();
    }

    public void Create(UserRecord user)
    {
        using var connection = _db.Open();
        connection.Execute(
            """
            INSERT INTO users(id, name, display, role, pwd_algo, pwd_salt, pwd_hash, pwd_iter, enabled, created_at, last_login_at)
            VALUES (@Id, @Name, @Display, @Role, @PwdAlgo, @PwdSalt, @PwdHash, @PwdIter, @Enabled, @CreatedAt, @LastLoginAt)
            """,
            user);
    }

    public void TouchLogin(string id)
    {
        using var connection = _db.Open();
        connection.Execute(
            "UPDATE users SET last_login_at = @at WHERE id = @id",
            new { at = DateTimeOffset.UtcNow.ToString("O"), id });
    }

    public bool UpdatePassword(string id, string salt, string hash, int iterations)
    {
        using var connection = _db.Open();
        return connection.Execute(
            """
            UPDATE users
            SET pwd_salt = @salt, pwd_hash = @hash, pwd_iter = @iterations
            WHERE id = @id
            """,
            new { id, salt, hash, iterations }) > 0;
    }
}
