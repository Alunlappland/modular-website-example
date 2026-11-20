using System.Diagnostics;
using Genkan.Backup;

namespace Genkan.Backup;

public sealed record KopiaResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    public string Combined => string.Join('\n', new[] { StdOut, StdErr }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// kopia CLI 的薄封装。所有调用都显式带上 config/cache/log/password，
/// 避免服务账户与用户账户的 %APPDATA% 差异。
/// </summary>
public sealed class KopiaService
{
    private readonly IConfiguration _config;
    private readonly SecretStore _secrets;
    private readonly GenkanPaths _paths;
    private readonly ILogger<KopiaService> _logger;

    public KopiaService(IConfiguration config, SecretStore secrets, GenkanPaths paths, ILogger<KopiaService> logger)
    {
        _config = config;
        _secrets = secrets;
        _paths = paths;
        _logger = logger;
    }

    public string ExePath => _config["backup:kopia:exePath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "scoop", "apps", "kopiaui", "current", "resources", "server", "kopia.exe");

    public string ConfigPath => _config["backup:kopia:configPath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kopia", "repository.config");

    public bool ExeExists => File.Exists(ExePath);

    public bool ConfigExists => File.Exists(ConfigPath);

    public bool HasPassword => _secrets.Has("kopia.password");

    public bool Ready => ExeExists && ConfigExists && HasPassword;

    public string StatusSummary
        => !ExeExists ? "找不到 kopia.exe"
        : !ConfigExists ? "找不到仓库配置文件"
        : !HasPassword ? "还没有保存仓库密码"
        : "就绪";

    public async Task<KopiaResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken, int timeoutSeconds = 600)
    {
        if (!ExeExists)
        {
            return new KopiaResult(-1, string.Empty, "找不到 kopia.exe：" + ExePath);
        }

        var password = _secrets.Get("kopia.password");
        if (string.IsNullOrEmpty(password))
        {
            return new KopiaResult(-1, string.Empty, "还没有保存 kopia 仓库密码。");
        }

        var start = new ProcessStartInfo(ExePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _paths.Root,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var logDir = Path.Combine(_paths.Logs, "kopia");
        var cacheDir = Path.Combine(_paths.Depend, "kopia-cache");

        start.Environment["KOPIA_CONFIG_PATH"] = ConfigPath;
        start.Environment["KOPIA_PASSWORD"] = password;
        start.Environment["KOPIA_CHECK_FOR_UPDATES"] = "false";
        start.Environment["KOPIA_LOG_DIR"] = logDir;
        start.Environment["KOPIA_CACHE_DIRECTORY"] = cacheDir;

        Directory.CreateDirectory(logDir);
        Directory.CreateDirectory(cacheDir);

        _logger.LogInformation("kopia {Args}", string.Join(' ', arguments));

        using var process = Process.Start(start);
        if (process is null)
        {
            return new KopiaResult(-1, string.Empty, "无法启动 kopia。");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // 忽略
            }

            return new KopiaResult(-2, string.Empty, "kopia 执行超时。");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new KopiaResult(process.ExitCode, stdout, stderr);
    }

    public IReadOnlyList<string> Sources()
    {
        var configured = _config.GetSection("backup:sources").Get<string[]>();
        if (configured is { Length: > 0 })
        {
            return configured.Where(Directory.Exists).Select(Path.GetFullPath).ToList();
        }

        return new[]
            {
                Path.Combine(_paths.Main, "data"),
                Path.Combine(_paths.Main, "document"),
                Path.Combine(_paths.Main, "conf"),
                Path.Combine(_paths.Main, "database"),
                Path.Combine(_paths.Main, "src"),
                Path.Combine(_paths.Main, "website"),
                _paths.Extend,
            }
            .Where(Directory.Exists)
            .ToList();
    }
}
