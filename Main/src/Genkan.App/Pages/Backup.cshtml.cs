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

    public IReadOnlyList<BackupRun> Runs { get; private set; } = Array.Empty<BackupRun>();

    public TimeSpan DailyTime => _backup.DailyTime;

    public int FullEveryDays => _backup.FullEveryDays;

    public int DaysSinceFull => _backup.DaysSinceLastFull();

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
        var (ok, message) = await _backup.RunAsync(normalized, HttpContext.RequestAborted);

        if (ok) Message = message;
        else Error = message;

        _audit.Write(HttpContext, "backup.run", normalized, ok ? "ok" : "fail");
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
    }
}
