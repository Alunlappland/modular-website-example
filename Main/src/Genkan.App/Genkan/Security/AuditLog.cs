using Dapper;
using Genkan.Data;

namespace Genkan.Security;

/// <summary>审计：登录、提权、发布、删除等关键动作，落库 + 落日志文件。</summary>
public sealed class AuditLog
{
    private readonly GenkanDb _db;
    private readonly ILogger<AuditLog> _logger;

    public AuditLog(GenkanDb db, ILogger<AuditLog> logger)
    {
        _db = db;
        _logger = logger;
    }

    public void Write(
        HttpContext? context,
        string action,
        string? target = null,
        string result = "ok",
        string? who = null,
        string? detail = null)
    {
        var ip = context?.Connection.RemoteIpAddress?.ToString();
        var ua = context?.Request.Headers.UserAgent.ToString();
        var actor = who
            ?? (context?.User.Identity?.IsAuthenticated == true ? context.User.Identity!.Name : "anonymous");

        try
        {
            using var connection = _db.Open();
            connection.Execute(
                """
                INSERT INTO audit(at, who, action, target, ip, ua, result, detail)
                VALUES (@at, @who, @action, @target, @ip, @ua, @result, @detail)
                """,
                new
                {
                    at = DateTimeOffset.UtcNow.ToString("O"),
                    who = actor,
                    action,
                    target,
                    ip,
                    ua,
                    result,
                    detail,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审计写入失败：{Action}", action);
        }

        _logger.LogInformation("AUDIT {Who} {Action} {Target} {Result}", actor, action, target, result);
    }
}
