namespace Genkan.Backup;

/// <summary>
/// 每天固定时间跑一次快照；距上次全量满 N 天时打 full 标签。
/// 只做「到点就跑」，不做补跑（错过就等下一次）。
/// </summary>
public sealed class BackupScheduler : BackgroundService
{
    private readonly BackupService _backup;
    private readonly CloudMirrorService _cloud;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupScheduler> _logger;

    public BackupScheduler(
        BackupService backup,
        CloudMirrorService cloud,
        IConfiguration config,
        ILogger<BackupScheduler> logger)
    {
        _backup = backup;
        _cloud = cloud;
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
                var time = _backup.DailyTime;
                var scheduledToday = new DateTimeOffset(now.Year, now.Month, now.Day, time.Hours, time.Minutes, 0, now.Offset);

                var lastDaily = _backup.LastSuccess("daily");
                var lastFull = _backup.LastSuccess("full");
                var lastSuccess = new[] { lastDaily, lastFull }
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Max();

                // 只要今天 12:00 之后还没有成功快照，就应该跑（手动提前跑过不影响）
                var dueToday = now >= scheduledToday && lastSuccess < scheduledToday;

                if (dueToday)
                {
                    var kind = _backup.DaysSinceLastFull() >= _backup.FullEveryDays ? "full" : "daily";
                    var (ok, message) = await _backup.RunAsync(kind, stoppingToken);
                    _logger.Log(ok ? LogLevel.Information : LogLevel.Warning, "计划备份（{Kind}）：{Message}", kind, message);

                    if (ok && _config.GetValue("backup:cloud:autoMirror", false) && _cloud.Ready)
                    {
                        var (mirrorOk, mirrorMessage) = await _cloud.MirrorAsync(stoppingToken);
                        _logger.Log(mirrorOk ? LogLevel.Information : LogLevel.Warning, "计划镜像：{Message}", mirrorMessage);
                    }
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
