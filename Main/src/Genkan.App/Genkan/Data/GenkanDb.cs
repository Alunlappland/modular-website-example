using Microsoft.Data.Sqlite;

namespace Genkan.Data;

/// <summary>
/// SQLite 连接工厂 + 建表。数据库文件在 <c>Main\database\genkan.db</c>。
/// 所有表都用 IF NOT EXISTS，启动时幂等执行；后续迁移用 settings 表里的 schema_version。
/// </summary>
public sealed class GenkanDb
{
    private readonly string _path;

    public string Path => _path;

    public GenkanDb(GenkanPaths paths)
    {
        Directory.CreateDirectory(paths.Database);
        _path = System.IO.Path.Combine(paths.Database, "genkan.db");
    }

    public SqliteConnection Open()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    public void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = Schema;
        command.ExecuteNonQuery();
    }

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS users (
            id            TEXT PRIMARY KEY,
            name          TEXT NOT NULL UNIQUE,
            display       TEXT NOT NULL,
            role          TEXT NOT NULL DEFAULT 'member',
            pwd_algo      TEXT NOT NULL DEFAULT 'pbkdf2-sha256',
            pwd_salt      TEXT NOT NULL,
            pwd_hash      TEXT NOT NULL,
            pwd_iter      INTEGER NOT NULL,
            enabled       INTEGER NOT NULL DEFAULT 1,
            created_at    TEXT NOT NULL,
            last_login_at TEXT
        );

        CREATE TABLE IF NOT EXISTS audit (
            id     INTEGER PRIMARY KEY AUTOINCREMENT,
            at     TEXT NOT NULL,
            who    TEXT,
            action TEXT NOT NULL,
            target TEXT,
            ip     TEXT,
            ua     TEXT,
            result TEXT,
            detail TEXT
        );

        CREATE INDEX IF NOT EXISTS ix_audit_at ON audit(at);

        CREATE TABLE IF NOT EXISTS settings (
            key        TEXT PRIMARY KEY,
            value      TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS comments (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            post_slug  TEXT NOT NULL,
            author     TEXT NOT NULL,
            body       TEXT NOT NULL,
            status     TEXT NOT NULL DEFAULT 'pending',
            ip         TEXT,
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_comments_post ON comments(post_slug, status);

        CREATE TABLE IF NOT EXISTS annotations (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            post_slug   TEXT NOT NULL,
            block_index INTEGER NOT NULL,
            block_hash  TEXT NOT NULL,
            author      TEXT NOT NULL,
            body        TEXT NOT NULL,
            status      TEXT NOT NULL DEFAULT 'pending',
            created_at  TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_annotations_post ON annotations(post_slug, status);

        CREATE TABLE IF NOT EXISTS reactions (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            post_slug   TEXT NOT NULL,
            kind        TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            created_at  TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_reactions ON reactions(post_slug, kind, fingerprint);

        INSERT INTO settings(key, value, updated_at)
        VALUES ('schema_version', '1', datetime('now'))
        ON CONFLICT(key) DO NOTHING;
        """;
}
