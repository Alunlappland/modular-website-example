using Genkan;

namespace Genkan.Extend;

/// <summary>
/// 监听 Extend 目录：新增/修改/删除挂载后自动重新扫描，不用重启、不用点按钮。
/// 只关心 extend.json 与目录结构；静态文件本来就是按请求从磁盘读的。
/// </summary>
public sealed class ExtendWatcher : BackgroundService
{
    private readonly GenkanPaths _paths;
    private readonly ExtendRegistry _registry;
    private readonly ILogger<ExtendWatcher> _logger;

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public ExtendWatcher(GenkanPaths paths, ExtendRegistry registry, ILogger<ExtendWatcher> logger)
    {
        _paths = paths;
        _registry = registry;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Directory.CreateDirectory(_paths.Extend);

            _watcher = new FileSystemWatcher(_paths.Extend)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            _watcher.Created += (_, _) => Schedule();
            _watcher.Changed += (_, _) => Schedule();
            _watcher.Deleted += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
            _watcher.Error += (_, args) =>
                _logger.LogWarning(args.GetException(), "Extend 监听出错");

            _logger.LogInformation("Extend 监听已启动：{Path}", _paths.Extend);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Extend 监听启动失败，挂载只能手动刷新");
        }

        return Task.CompletedTask;
    }

    private void Schedule()
    {
        // 编辑器保存常常触发多次事件，压一下再扫描
        _debounce?.Dispose();
        _debounce = new Timer(_ => Reload(), null, 700, Timeout.Infinite);
    }

    private void Reload()
    {
        try
        {
            _registry.Reload(_paths);
            _logger.LogInformation("Extend 目录变化，已重新扫描");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "重新扫描 Extend 失败");
        }
    }

    public override void Dispose()
    {
        _debounce?.Dispose();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        base.Dispose();
    }
}
