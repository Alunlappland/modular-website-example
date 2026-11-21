using System.Text.Json;
using Dapper;
using Genkan.Data;

namespace Genkan.Backup;

public sealed class BackupRun
{
    public long Id { get; set; }
    public string Kind { get; set; } = "daily";
    public string State { get; set; } = "running";
    public string StartedAt { get; set; } = string.Empty;
    public string? EndedAt { get; set; }
    public string? SnapshotId { get; set; }
    public long? Files { get; set; }
    public long? Bytes { get; set; }
    public string? Error { get; set; }
    public string? Output { get; set; }
}

public sealed record SnapshotInfo(string Id, string Source, string StartTime, long Files, long Bytes, string Tags);

public sealed record RetentionPlan(IReadOnlyList<SnapshotInfo> Keep, IReadOnlyList<SnapshotInfo> Expire);

/// <summary>执行备份、记录历史、列出快照。</summary>
public sealed class BackupService
{
    private readonly KopiaService _kopia;
    private readonly GenkanDb _db;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackupService(KopiaService kopia, GenkanDb db, IConfiguration config, ILogger<BackupService> logger)
    {
        _kopia = kopia;
        _db = db;
        _config = config;
        _logger = logger;
    }

    public bool Busy => _gate.CurrentCount == 0;

    public long BeginExternalRun(string kind) => StartRun(kind);

    public void EndExternalRun(long id, bool ok, string? error, string? output)
        => FinishRun(id, ok, null, null, null, error, output);

    public async Task<(bool Ok, string Message)> RunAsync(string kind, CancellationToken cancellationToken)
    {
        if (!_kopia.Ready)
        {
            return (false, _kopia.StatusSummary);
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return (false, "已经有一个备份在跑。");
        }

        var runId = StartRun(kind);

        try
        {
            var sources = _kopia.Sources();
            if (sources.Count == 0)
            {
                FinishRun(runId, false, null, null, null, "没有可备份的目录。", null);
                return (false, "没有可备份的目录。");
            }

            var arguments = new List<string> { "snapshot", "create" };
            arguments.AddRange(sources);
            arguments.Add("--json");
            arguments.Add("--tags");
            arguments.Add("app:genkan");
            arguments.Add("--tags");
            arguments.Add("kind:" + kind);

            var result = await _kopia.RunAsync(arguments, cancellationToken);

            var (snapshotId, files, bytes) = ParseCreate(result.StdOut);
            var error = result.Ok ? null : FirstError(result);
            FinishRun(runId, result.Ok, snapshotId, files, bytes, error, Truncate(result.Combined, 8000));

            if (!result.Ok)
            {
                return (false, error ?? "备份失败。");
            }

            return (true, $"备份完成：{files ?? 0} 个文件，{FormatBytes(bytes ?? 0)}。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "备份执行异常");
            FinishRun(runId, false, null, null, null, ex.Message, null);
            return (false, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(CancellationToken cancellationToken)
    {
        if (!_kopia.Ready)
        {
            return Array.Empty<SnapshotInfo>();
        }

        var result = await _kopia.RunAsync(new[] { "snapshot", "list", "--all", "--json" }, cancellationToken, 120);
        if (!result.Ok)
        {
            _logger.LogWarning("列举快照失败：{Error}", result.StdErr);
            return Array.Empty<SnapshotInfo>();
        }

        return ParseSnapshots(result.StdOut);
    }

    public async Task<(bool Ok, string Message)> RestoreAsync(string snapshotId, string target, CancellationToken cancellationToken)
    {
        if (!_kopia.Ready)
        {
            return (false, _kopia.StatusSummary);
        }

        if (string.IsNullOrWhiteSpace(snapshotId) || string.IsNullOrWhiteSpace(target))
        {
            return (false, "快照 ID 和目标目录都要填。");
        }

        Directory.CreateDirectory(target);
        var result = await _kopia.RunAsync(new[] { "restore", snapshotId, target }, cancellationToken);
        return result.Ok
            ? (true, "已恢复到 " + target)
            : (false, FirstError(result) ?? "恢复失败。");
    }

    public IReadOnlyList<BackupRun> RecentRuns(int limit = 20)
    {
        using var connection = _db.Open();
        return connection.Query<BackupRun>(
            "SELECT * FROM backup_runs ORDER BY id DESC LIMIT @limit",
            new { limit }).ToList();
    }

    public DateTimeOffset? LastSuccess(string kind)
    {
        using var connection = _db.Open();
        var value = connection.ExecuteScalar<string?>(
            "SELECT started_at FROM backup_runs WHERE kind = @kind AND state = 'success' ORDER BY id DESC LIMIT 1",
            new { kind });

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    public int DaysSinceLastFull()
    {
        var last = LastSuccess("full");
        return last is null ? int.MaxValue : (int)(DateTimeOffset.UtcNow - last.Value).TotalDays;
    }

    /// <summary>
    /// 保留策略预览：保留最近 6 个 full 恢复点 + 最近 10 天的日快照，其余列为可清理。
    /// 只做预览，不自动删除。
    /// </summary>
    public RetentionPlan PlanRetention(IReadOnlyList<SnapshotInfo> snapshots)
    {
        var keep = new List<SnapshotInfo>();
        var expire = new List<SnapshotInfo>();

        var fullKeep = snapshots
            .Where(s => IsFull(s))
            .OrderByDescending(s => s.StartTime, StringComparer.Ordinal)
            .Take(6)
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-10);

        foreach (var snapshot in snapshots)
        {
            var recent = DateTimeOffset.TryParse(snapshot.StartTime, out var at) && at > cutoff;
            var keepThis = IsFull(snapshot) ? fullKeep.Contains(snapshot.Id) : recent;

            if (keepThis) keep.Add(snapshot);
            else expire.Add(snapshot);
        }

        return new RetentionPlan(keep, expire);
    }

    private static bool IsFull(SnapshotInfo snapshot)
        => snapshot.Tags.Contains("kind:full", StringComparison.OrdinalIgnoreCase);

    public int FullEveryDays => Math.Max(1, _config.GetValue("backup:schedule:fullEveryDays", 10));

    public TimeSpan DailyTime
    {
        get
        {
            var raw = _config["backup:schedule:time"] ?? "12:00";
            return TimeSpan.TryParse(raw, out var parsed) ? parsed : new TimeSpan(12, 0, 0);
        }
    }

    private long StartRun(string kind)
    {
        using var connection = _db.Open();
        return connection.ExecuteScalar<long>(
            """
            INSERT INTO backup_runs(kind, state, started_at) VALUES (@kind, 'running', @at);
            SELECT last_insert_rowid();
            """,
            new { kind, at = DateTimeOffset.UtcNow.ToString("O") });
    }

    private void FinishRun(long id, bool ok, string? snapshotId, long? files, long? bytes, string? error, string? output)
    {
        using var connection = _db.Open();
        connection.Execute(
            """
            UPDATE backup_runs
            SET state = @state, ended_at = @endedAt, snapshot_id = @snapshotId,
                files = @files, bytes = @bytes, error = @error, output = @output
            WHERE id = @id
            """,
            new
            {
                id,
                state = ok ? "success" : "failed",
                endedAt = DateTimeOffset.UtcNow.ToString("O"),
                snapshotId,
                files,
                bytes,
                error,
                output,
            });
    }

    private static (string? Id, long? Files, long? Bytes) ParseCreate(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Reverse())
        {
            if (!line.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;

                long? files = null;
                long? bytes = null;
                if (root.TryGetProperty("stats", out var stats))
                {
                    if (stats.TryGetProperty("fileCount", out var fileCount) && fileCount.TryGetInt64(out var fc)) files = fc;
                    if (stats.TryGetProperty("totalSize", out var totalSize) && totalSize.TryGetInt64(out var ts)) bytes = ts;
                }

                return (id, files, bytes);
            }
            catch
            {
                // 继续找上一行
            }
        }

        return (null, null, null);
    }

    private static IReadOnlyList<SnapshotInfo> ParseSnapshots(string json)
    {
        var list = new List<SnapshotInfo>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? string.Empty : string.Empty;
                var start = item.TryGetProperty("startTime", out var startValue) ? startValue.GetString() ?? string.Empty : string.Empty;

                var source = string.Empty;
                if (item.TryGetProperty("source", out var sourceElement) &&
                    sourceElement.TryGetProperty("path", out var pathValue))
                {
                    source = pathValue.GetString() ?? string.Empty;
                }

                long files = 0;
                long bytes = 0;
                if (item.TryGetProperty("stats", out var stats))
                {
                    if (stats.TryGetProperty("fileCount", out var fc) && fc.TryGetInt64(out var f)) files = f;
                    if (stats.TryGetProperty("totalSize", out var ts) && ts.TryGetInt64(out var b)) bytes = b;
                }

                var tags = string.Empty;
                if (item.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                {
                    tags = string.Join(',', tagsElement.EnumerateArray().Select(t => t.GetString()).Where(t => t is not null));
                }

                list.Add(new SnapshotInfo(id, source, start, files, bytes, tags));
            }
        }
        catch
        {
            // 解析失败按空处理
        }

        return list.OrderByDescending(s => s.StartTime, StringComparer.Ordinal).ToList();
    }

    private static string? FirstError(KopiaResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line ?? "kopia 返回非零退出码。";
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    public static string FormatBytes(long bytes)
        => bytes >= 1024L * 1024 * 1024 ? (bytes / 1024.0 / 1024 / 1024).ToString("F2") + " GB"
         : bytes >= 1024 * 1024 ? (bytes / 1024.0 / 1024).ToString("F1") + " MB"
         : (bytes / 1024.0).ToString("F0") + " KB";
}
