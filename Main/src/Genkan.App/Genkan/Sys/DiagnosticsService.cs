using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Dapper;
using Genkan.Ai;
using Genkan.Backup;
using Genkan.Data;
using Genkan.Sync;

namespace Genkan.Sys;

public sealed record DiagnosticCheck(string Name, string Status, string Detail);

/// <summary>
/// 自诊断：把「能不能正常用」拆成一组可读的检查项。
/// 只读，不修改任何东西；有一项失败也不影响其它项。
/// </summary>
public sealed class DiagnosticsService
{
    private readonly GenkanPaths _paths;
    private readonly GenkanDb _db;
    private readonly KopiaService _kopia;
    private readonly BackupService _backup;
    private readonly CloudMirrorService _cloud;
    private readonly SyncService _sync;
    private readonly AiService _ai;

    public DiagnosticsService(
        GenkanPaths paths,
        GenkanDb db,
        KopiaService kopia,
        BackupService backup,
        CloudMirrorService cloud,
        SyncService sync,
        AiService ai)
    {
        _paths = paths;
        _db = db;
        _kopia = kopia;
        _backup = backup;
        _cloud = cloud;
        _sync = sync;
        _ai = ai;
    }

    public async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(CancellationToken cancellationToken)
    {
        var checks = new List<DiagnosticCheck>();

        // 数据库
        try
        {
            using var connection = _db.Open();
            var files = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM files");
            var users = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM users");
            var size = new FileInfo(Path.Combine(_paths.Database, "genkan.db")).Length / 1024.0 / 1024;
            checks.Add(new DiagnosticCheck("数据库", "ok", $"{users} 个账号 · {files} 个文件记录 · {size:F2} MB"));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("数据库", "fail", ex.Message));
        }

        // 磁盘
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var mainDrive = new DriveInfo(Path.GetPathRoot(_paths.Root)!);
                var free = mainDrive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                var total = mainDrive.TotalSize / 1024.0 / 1024 / 1024;
                checks.Add(new DiagnosticCheck(
                    "系统盘",
                    free < 5 ? "warn" : "ok",
                    $"剩余 {free:F1} GB / 共 {total:F0} GB"));
            }
            else
            {
                var drive = new DriveInfo("/");
                var free = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                var total = drive.TotalSize / 1024.0 / 1024 / 1024;
                checks.Add(new DiagnosticCheck(
                    "根分区",
                    free < 5 ? "warn" : "ok",
                    $"剩余 {free:F1} GB / 共 {total:F0} GB"));
            }

            var repository = _kopia.RepositoryPath();
            if (!string.IsNullOrWhiteSpace(repository) && Directory.Exists(repository))
            {
                checks.Add(new DiagnosticCheck("备份仓库", "ok", repository));
            }
            else
            {
                checks.Add(new DiagnosticCheck("备份仓库", "warn", "读不到 kopia 仓库路径"));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("磁盘", "fail", ex.Message));
        }

        // 备份
        try
        {
            var lastDaily = _backup.LastSuccess("daily");
            var lastFull = _backup.LastSuccess("full");
            var last = new[] { lastDaily, lastFull }.Where(value => value.HasValue).Select(value => value!.Value)
                .DefaultIfEmpty(DateTimeOffset.MinValue).Max();

            if (last == DateTimeOffset.MinValue)
            {
                checks.Add(new DiagnosticCheck("备份", "fail", "还没有成功过任何快照"));
            }
            else
            {
                var age = DateTimeOffset.UtcNow - last;
                var status = age.TotalHours > 36 ? "warn" : "ok";
                checks.Add(new DiagnosticCheck(
                    "备份",
                    status,
                    $"最近一次 {last.ToLocalTime():MM-dd HH:mm}（{age.TotalHours:F0} 小时前）· " +
                    $"距上次全量 {_backup.DaysSinceLastFull()} 天"));
            }

            checks.Add(new DiagnosticCheck(
                "kopia",
                _kopia.HasPassword && _kopia.ExeExists && _kopia.ConfigExists ? "ok" : "warn",
                _kopia.StatusSummary));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("备份", "fail", ex.Message));
        }

        // 网盘镜像
        try
        {
            checks.Add(new DiagnosticCheck(
                "R2 镜像",
                _cloud.Ready ? "ok" : "warn",
                _cloud.Ready ? $"目标 {_cloud.Target}" : _cloud.StatusSummary));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("R2 镜像", "fail", ex.Message));
        }

        // Syncthing
        try
        {
            var status = await _sync.GetStatusAsync(cancellationToken);
            if (status is null)
            {
                checks.Add(new DiagnosticCheck("同步", "warn", "Syncthing 没响应（未安装或未启动）"));
            }
            else
            {
                var folders = await _sync.GetFoldersAsync(cancellationToken);
                var devices = await _sync.GetDevicesAsync(cancellationToken);
                checks.Add(new DiagnosticCheck(
                    "同步",
                    "ok",
                    $"{status.DeviceName} · {folders?.Count ?? 0} 个文件夹 · {devices?.Count ?? 0} 台设备"));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("同步", "fail", ex.Message));
        }

        // 内网/虚拟网卡直连地址
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .Distinct()
                .ToList();

            checks.Add(new DiagnosticCheck(
                "直连地址",
                addresses.Count > 0 ? "ok" : "warn",
                addresses.Count > 0
                    ? string.Join(" · ", addresses.Select(ip => $"http://{ip}:8080"))
                    : "没有可用的内网地址"));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("直连地址", "warn", ex.Message));
        }

        // AI
        try
        {
            var providers = _ai.Catalog.Providers;
            var ready = providers.Count(provider => _ai.HasKey(provider.Id));
            checks.Add(new DiagnosticCheck(
                "AI",
                ready > 0 ? "ok" : "warn",
                $"{ready} / {providers.Count} 家已配置 Key"));
        }
        catch (Exception ex)
        {
            checks.Add(new DiagnosticCheck("AI", "fail", ex.Message));
        }

        // 今天的错误日志条数
        try
        {
            var logPath = Path.Combine(_paths.Logs, $"app-{DateTime.Now:yyyyMMdd}.log");
            if (File.Exists(logPath))
            {
                var errors = 0;

                // Serilog 正开着这个文件，必须用共享读写方式打开，否则会被锁挡住
                using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                {
                    while (reader.ReadLine() is { } line)
                    {
                        if (line.Contains("[ERR]", StringComparison.Ordinal))
                        {
                            errors++;
                        }
                    }
                }

                checks.Add(new DiagnosticCheck("日志", errors > 20 ? "warn" : "ok", $"今天 {errors} 条 ERR"));
            }
            else
            {
                checks.Add(new DiagnosticCheck("日志", "ok", "今天还没有日志文件"));
            }
        }
        catch (Exception ex)
        {
            // 日志读不到不影响站点本身，标注一下就好
            checks.Add(new DiagnosticCheck("日志", "warn", "读日志时出错：" + ex.Message));
        }

        return checks;
    }
}
