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

        // 轻量迁移：给老库补列（SQLite 不支持 ADD COLUMN IF NOT EXISTS）
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(gallery_favorites)";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(1));
            }
        }

        if (!columns.Contains("collection_id"))
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE gallery_favorites ADD COLUMN collection_id TEXT";
            alter.ExecuteNonQuery();
        }

        var clipColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var info = connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(clips)";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                clipColumns.Add(reader.GetString(1));
            }
        }

        if (!clipColumns.Contains("pinned"))
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE clips ADD COLUMN pinned INTEGER NOT NULL DEFAULT 0";
            alter.ExecuteNonQuery();
        }

        if (!columns.Contains("local_path"))
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE gallery_favorites ADD COLUMN local_path TEXT";
            alter.ExecuteNonQuery();
        }
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

        CREATE TABLE IF NOT EXISTS files (
            id         TEXT PRIMARY KEY,
            owner      TEXT NOT NULL,
            name       TEXT NOT NULL,
            rel_path   TEXT NOT NULL,
            size       INTEGER NOT NULL,
            sha256     TEXT,
            mime       TEXT,
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_files_owner ON files(owner, created_at);

        CREATE TABLE IF NOT EXISTS shares (
            id            TEXT PRIMARY KEY,
            owner         TEXT NOT NULL,
            kind          TEXT NOT NULL,
            file_id       TEXT,
            token         TEXT NOT NULL,
            password_hash TEXT,
            expires_at    TEXT,
            max_downloads INTEGER,
            downloads     INTEGER NOT NULL DEFAULT 0,
            dir           TEXT,
            created_at    TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_shares_token ON shares(token);

        CREATE TABLE IF NOT EXISTS backup_runs (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            kind        TEXT NOT NULL,
            state       TEXT NOT NULL,
            started_at  TEXT NOT NULL,
            ended_at    TEXT,
            snapshot_id TEXT,
            files       INTEGER,
            bytes       INTEGER,
            error       TEXT,
            output      TEXT
        );

        CREATE INDEX IF NOT EXISTS ix_backup_started ON backup_runs(started_at);

        CREATE TABLE IF NOT EXISTS api_keys (
            id           TEXT PRIMARY KEY,
            owner_id     TEXT NOT NULL,
            name         TEXT NOT NULL,
            key_hash     TEXT NOT NULL UNIQUE,
            scopes       TEXT NOT NULL,
            created_at   TEXT NOT NULL,
            last_used_at TEXT,
            revoked_at   TEXT
        );

        CREATE INDEX IF NOT EXISTS ix_api_keys_owner ON api_keys(owner_id);

        CREATE TABLE IF NOT EXISTS clips (
            id          TEXT PRIMARY KEY,
            owner       TEXT NOT NULL,
            text        TEXT NOT NULL,
            created_at  TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_clips_owner ON clips(owner, created_at);

        CREATE TABLE IF NOT EXISTS gallery_favorites (
            id           TEXT PRIMARY KEY,
            owner        TEXT NOT NULL,
            image_key    TEXT NOT NULL,
            preview_url  TEXT NOT NULL,
            original_url TEXT NOT NULL,
            source       TEXT NOT NULL,
            tags         TEXT,
            created_at   TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_gallery_favorite ON gallery_favorites(owner, image_key);

        CREATE TABLE IF NOT EXISTS gallery_blocked_tags (
            id         TEXT PRIMARY KEY,
            owner      TEXT NOT NULL,
            tag        TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_gallery_blocked ON gallery_blocked_tags(owner, tag);

        CREATE TABLE IF NOT EXISTS gallery_collections (
            id         TEXT PRIMARY KEY,
            owner      TEXT NOT NULL,
            name       TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_gallery_collections ON gallery_collections(owner, created_at);

        CREATE TABLE IF NOT EXISTS gallery_tag_history (
            id         TEXT PRIMARY KEY,
            owner      TEXT NOT NULL,
            query      TEXT NOT NULL,
            source     TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        INSERT INTO settings(key, value, updated_at)
        VALUES ('schema_version', '1', datetime('now'))
        ON CONFLICT(key) DO NOTHING;
        """;
}
