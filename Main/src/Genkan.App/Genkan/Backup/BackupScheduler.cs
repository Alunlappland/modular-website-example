namespace Genkan.Backup;

/// <summary>
/// 每天固定时间跑一次快照；距上次全量满 N 天时打 full 标签。
/// 只做「到点就跑」，不做补跑（错过就等下一次）。
/// </summary>
public sealed class BackupScheduler : BackgroundService
{
    private readonly BackupService _backup;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupScheduler> _logger;

    public BackupScheduler(BackupService backup, IConfiguration config, ILogger<BackupScheduler> logger)
    {
        _backup = backup;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("backup:schedule:enabled", true))
        {
            _logger.LogInformation("备份计划任务已关闭。");
            return;
        }

        _logger.LogInformation("备份计划任务已启动：每天 {Time}，每 {Days} 天一次全量。",
            _backup.DailyTime, _backup.FullEveryDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                var todayRun = _backup.LastSuccess("daily");
                var todayFull = _backup.LastSuccess("full");

                var dueToday = now.TimeOfDay >= _backup.DailyTime
                               && (todayRun is null || todayRun.Value.ToLocalTime().Date < now.Date)
                               && (todayFull is null || todayFull.Value.ToLocalTime().Date < now.Date);

                if (dueToday)
                {
                    var kind = _backup.DaysSinceLastFull() >= _backup.FullEveryDays ? "full" : "daily";
                    var (ok, message) = await _backup.RunAsync(kind, stoppingToken);
                    _logger.Log(ok ? LogLevel.Information : LogLevel.Warning, "计划备份（{Kind}）：{Message}", kind, message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "计划备份执行失败。");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(40), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
