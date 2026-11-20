using Genkan.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Genkan.Pages;

[Authorize]
public class DashboardModel : PageModel
{
    private readonly SysMonitor _monitor;

    public DashboardModel(SysMonitor monitor) => _monitor = monitor;

    public SysSnapshot Snapshot { get; private set; } = default!;

    public string UptimeText => FormatUptime(Snapshot.UptimeSeconds);

    public string MemoryText =>
        $"{Snapshot.MemoryFreeMb / 1024.0:F1} GB 可用 / {Snapshot.MemoryTotalMb / 1024.0:F1} GB";

    public void OnGet() => Snapshot = _monitor.Sample();

    private static string FormatUptime(long seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        if (span.Days > 0) return $"{span.Days} 天 {span.Hours} 小时";
        if (span.Hours > 0) return $"{span.Hours} 小时 {span.Minutes} 分";
        return $"{span.Minutes} 分";
    }
}
