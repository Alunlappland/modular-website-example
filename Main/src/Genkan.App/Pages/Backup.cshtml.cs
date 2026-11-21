using System.Text;
using System.Text.Json;
using Genkan.Backup;
using Genkan.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize(Roles = "admin")]
public class BackupModel : PageModel
{
    private readonly KopiaService _kopia;
    private readonly SecretStore _secrets;
    private readonly BackupService _backup;
    private readonly GenkanPaths _paths;
    private readonly AuditLog _audit;

    public BackupModel(
        KopiaService kopia,
        SecretStore secrets,
        BackupService backup,
        GenkanPaths paths,
        AuditLog audit)
    {
        _kopia = kopia;
        _secrets = secrets;
        _backup = backup;
        _paths = paths;
        _audit = audit;
    }

    public bool Ready { get; private set; }

    public string StatusSummary { get; private set; } = string.Empty;

    public string ExePath { get; private set; } = string.Empty;

    public string ConfigPath { get; private set; } = string.Empty;

    public bool ExeExists { get; private set; }

    public bool ConfigExists { get; private set; }

    public bool HasPassword { get; private set; }

    public IReadOnlyList<string> Sources { get; private set; } = Array.Empty<string>();

    public IReadOnlyList<SnapshotInfo> Snapshots { get; private set; } = Array.Empty<SnapshotInfo>();

    public RetentionPlan? Retention { get; private set; }

    public IReadOnlyList<BackupRun> Runs { get; private set; } = Array.Empty<BackupRun>();

    public TimeSpan DailyTime => _backup.DailyTime;

    public int FullEveryDays => _backup.FullEveryDays;

    public int DaysSinceFull => _backup.DaysSinceLastFull();

    public bool Busy => _backup.Busy;

    public string StagingPath => Path.Combine(_paths.Data, "restore-staging");

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? SnapshotId { get; set; }

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var value = (Password ?? string.Empty).Trim();
        if (value.Length < 4)
        {
            Error = "密码太短。";
            await LoadAsync();
            return Page();
        }

        _secrets.Set("kopia.password", value);
        _audit.Write(HttpContext, "backup.password", "kopia", "ok");
        Message = "仓库密码已用 DataProtection 加密保存。";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostForgetAsync()
    {
        _secrets.Remove("kopia.password");
        _audit.Write(HttpContext, "backup.password.remove", "kopia", "ok");
        Message = "已清除保存的仓库密码。";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRunAsync(string kind)
    {
        var normalized = kind is "full" ? "full" : "daily";

        if (_backup.Busy)
        {
            Message = "已经有备份在跑，等它结束。";
            await LoadAsync();
            return Page();
        }

        // 不绑定 HTTP 请求：刷新/关页面不会中断 kopia
        _ = Task.Run(async () =>
        {
            try
            {
                await _backup.RunAsync(normalized, CancellationToken.None);
            }
            catch
            {
                // 失败已写进 backup_runs
            }
        });

        _audit.Write(HttpContext, "backup.run", normalized, "started");
        Message = "备份已在后台开始，约十几秒后刷新本页看历史。";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync()
    {
        var target = Path.Combine(StagingPath, DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        var (ok, message) = await _backup.RestoreAsync(SnapshotId ?? string.Empty, target, HttpContext.RequestAborted);

        if (ok) Message = message;
        else Error = message;

        _audit.Write(HttpContext, "backup.restore", SnapshotId, ok ? "ok" : "fail");
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostExportKitAsync()
    {
        var password = _secrets.Get("kopia.password");
        if (string.IsNullOrEmpty(password))
        {
            Error = "还没有保存仓库密码，无法导出恢复包。";
            await LoadAsync();
            return Page();
        }

        var repoPath = ReadRepositoryPath();
        var kit = new StringBuilder()
            .AppendLine("# 玄关 · kopia 恢复包")
            .AppendLine()
            .AppendLine($"> 生成时间：{DateTimeOffset.Now:yyyy-MM-dd HH:mm}")
            .AppendLine($"> 仓库类型：filesystem")
            .AppendLine($"> 仓库路径：`{repoPath}`")
            .AppendLine($"> kopia 配置：`{_kopia.ConfigPath}`")
            .AppendLine($"> kopia 程序：`{_kopia.ExePath}`")
            .AppendLine()
            .AppendLine("## 仓库密码（明文，请离线保存）")
            .AppendLine()
            .AppendLine("```")
            .AppendLine(password)
            .AppendLine("```")
            .AppendLine()
            .AppendLine("## 恢复步骤")
            .AppendLine()
            .AppendLine("1. 安装 kopia（scoop install kopia 或官网安装包）。")
            .AppendLine("2. 把仓库目录复制回本机（若从网盘恢复）。")
            .AppendLine("3. 连接仓库：")
            .AppendLine()
            .AppendLine("```powershell")
            .AppendLine($"kopia repository connect filesystem --path \"{repoPath}\" --config-file \"{_kopia.ConfigPath}\"")
            .AppendLine("```")
            .AppendLine()
            .AppendLine("4. 列出快照：`kopia snapshot list --all`")
            .AppendLine("5. 恢复整份：`kopia restore <快照ID> D:\\restore-target`")
            .AppendLine()
            .AppendLine("> 密码泄露等于仓库泄露。建议打印或存进密码管理器，不要留在站点目录里。");

        var file = Path.Combine(_paths.Conf, $"recovery-kit-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md");
        await System.IO.File.WriteAllTextAsync(file, kit.ToString(), Encoding.UTF8);

        _audit.Write(HttpContext, "backup.export-kit", "recovery-kit", "ok");
        Message = "恢复包已导出：" + file + "（含明文密码，请尽快离线保存后删除）。";
        await LoadAsync();
        return Page();
    }

    private string ReadRepositoryPath()
    {
        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(_kopia.ConfigPath));
            if (doc.RootElement.TryGetProperty("storage", out var storage) &&
                storage.TryGetProperty("config", out var config) &&
                config.TryGetProperty("path", out var path))
            {
                return path.GetString() ?? "(未知)";
            }
        }
        catch
        {
            // 配置读不出来就显示未知
        }

        return "(未知)";
    }

    private async Task LoadAsync()
    {
        ExePath = _kopia.ExePath;
        ConfigPath = _kopia.ConfigPath;
        ExeExists = _kopia.ExeExists;
        ConfigExists = _kopia.ConfigExists;
        HasPassword = _kopia.HasPassword;
        Ready = _kopia.Ready;
        StatusSummary = _kopia.StatusSummary;
        Sources = _kopia.Sources();

        Runs = _backup.RecentRuns();

        Snapshots = Ready ? await _backup.ListSnapshotsAsync(HttpContext.RequestAborted) : Array.Empty<SnapshotInfo>();

        if (Snapshots.Count > 0)
        {
            Retention = _backup.PlanRetention(Snapshots);
        }
    }
}
