using System.Diagnostics;
using System.Text;
using Genkan.Backup;

namespace Genkan.Backup;

/// <summary>
/// 用 rclone 把 kopia 仓库镜像到 Cloudflare R2（S3 兼容）。
/// 凭据存 SecretStore，运行时生成一份临时 rclone.conf，不改用户的全局配置。
/// </summary>
public sealed class CloudMirrorService
{
    private readonly IConfiguration _config;
    private readonly SecretStore _secrets;
    private readonly KopiaService _kopia;
    private readonly GenkanPaths _paths;
    private readonly BackupService _backup;
    private readonly ILogger<CloudMirrorService> _logger;

    public CloudMirrorService(
        IConfiguration config,
        SecretStore secrets,
        KopiaService kopia,
        GenkanPaths paths,
        BackupService backup,
        ILogger<CloudMirrorService> logger)
    {
        _config = config;
        _secrets = secrets;
        _kopia = kopia;
        _paths = paths;
        _backup = backup;
        _logger = logger;
    }

    public bool Enabled => _config.GetValue("backup:cloud:enabled", false);

    public string RclonePath => _config["backup:cloud:rclonePath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "shims", "rclone.exe");

    public string Remote => _config["backup:cloud:remote"] ?? "r2";

    public string Endpoint => (_config["backup:cloud:endpoint"] ?? string.Empty).TrimEnd('/');

    public string Bucket => _config["backup:cloud:bucket"] ?? string.Empty;

    public string Prefix => (_config["backup:cloud:prefix"] ?? "kopia").Trim('/');

    public string ConfigPath => Path.Combine(_paths.Conf, "rclone.conf");

    public bool HasCredentials => _secrets.Has("r2.accessKeyId") && _secrets.Has("r2.secretAccessKey");

    public bool RcloneExists => File.Exists(RclonePath);

    public bool Ready => Enabled && HasCredentials && RcloneExists && !string.IsNullOrEmpty(Endpoint) && !string.IsNullOrEmpty(Bucket);

    public string StatusSummary
        => !Enabled ? "未启用"
        : !RcloneExists ? "找不到 rclone.exe"
        : !HasCredentials ? "还没有保存 R2 凭据"
        : string.IsNullOrEmpty(Endpoint) || string.IsNullOrEmpty(Bucket) ? "端点或桶名未配置"
        : "就绪";

    public string Target
        => string.IsNullOrEmpty(Prefix) ? $"{Remote}:{Bucket}" : $"{Remote}:{Bucket}/{Prefix}";

    public async Task<KopiaResult> TestAsync(CancellationToken cancellationToken)
    {
        if (!Ready)
        {
            return new KopiaResult(-1, string.Empty, StatusSummary);
        }

        WriteConfig();
        return await RunAsync(new[] { "lsd", $"{Remote}:" }, cancellationToken, 60);
    }

    public async Task<(bool Ok, string Message)> MirrorAsync(CancellationToken cancellationToken)
    {
        if (!Ready)
        {
            return (false, StatusSummary);
        }

        if (Interlocked.CompareExchange(ref _mirroring, 1, 0) != 0)
        {
            return (false, "已经有一次镜像在跑。");
        }

        try
        {
            return await MirrorCoreAsync(cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _mirroring, 0);
        }
    }

    private int _mirroring;

    public bool Mirroring => Volatile.Read(ref _mirroring) == 1;

    private async Task<(bool Ok, string Message)> MirrorCoreAsync(CancellationToken cancellationToken)
    {
        var repository = _kopia.RepositoryPath();
        if (string.IsNullOrEmpty(repository) || !Directory.Exists(repository))
        {
            return (false, "读不到 kopia 仓库路径（" + repository + "）。");
        }

        WriteConfig();

        var runId = _backup.BeginExternalRun("mirror");
        var arguments = new List<string>
        {
            "sync", repository, Target,
            "--config", ConfigPath,
            "--fast-list",
            "--transfers", "4",
            "--checkers", "8",
            // 保险丝：本地仓库异常变空时，最多删云端 50 个文件就停
            "--max-delete", "50",
            "--stats-one-line",
        };

        var result = await RunAsync(arguments, cancellationToken, 1800);

        var message = result.Ok
            ? "镜像完成：" + FirstLine(result.StdOut)
            : (FirstLine(result.StdErr) is { Length: > 0 } line ? line : "镜像失败。");

        _backup.EndExternalRun(runId, result.Ok, result.Ok ? null : message, Truncate(result.Combined, 8000));
        _logger.Log(result.Ok ? LogLevel.Information : LogLevel.Warning, "R2 镜像：{Message}", message);

        return (result.Ok, message);
    }

    public void SaveCredentials(string accessKeyId, string secretAccessKey)
    {
        _secrets.Set("r2.accessKeyId", accessKeyId.Trim());
        _secrets.Set("r2.secretAccessKey", secretAccessKey.Trim());
    }

    public void ClearCredentials()
    {
        _secrets.Remove("r2.accessKeyId");
        _secrets.Remove("r2.secretAccessKey");
        if (File.Exists(ConfigPath))
        {
            File.Delete(ConfigPath);
        }
    }

    /// <summary>生成只给本站用的 rclone 配置（含明文密钥，放在 conf 下，已被 .gitignore 排除）。</summary>
    public void WriteConfig()
    {
        var accessKeyId = _secrets.Get("r2.accessKeyId") ?? string.Empty;
        var secretAccessKey = _secrets.Get("r2.secretAccessKey") ?? string.Empty;

        var builder = new StringBuilder()
            .AppendLine($"[{Remote}]")
            .AppendLine("type = s3")
            .AppendLine("provider = Cloudflare")
            .AppendLine($"access_key_id = {accessKeyId}")
            .AppendLine($"secret_access_key = {secretAccessKey}")
            .AppendLine($"endpoint = {Endpoint}")
            .AppendLine("region = auto")
            .AppendLine("acl = private");

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, builder.ToString(), Encoding.UTF8);
    }

    private async Task<KopiaResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken, int timeoutSeconds)
    {
        if (!File.Exists(RclonePath))
        {
            return new KopiaResult(-1, string.Empty, "找不到 rclone.exe：" + RclonePath);
        }

        var start = new ProcessStartInfo(RclonePath)
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

        using var process = Process.Start(start);
        if (process is null)
        {
            return new KopiaResult(-1, string.Empty, "无法启动 rclone。");
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
            try { process.Kill(entireProcessTree: true); } catch { }
            return new KopiaResult(-2, string.Empty, "rclone 执行超时。");
        }

        return new KopiaResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FirstLine(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
