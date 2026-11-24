using System.Security.Claims;

namespace Genkan.Security;

/// <summary>
/// 取当前登录身份。没有身份时**不伪造**一个 "unknown" 用户，
/// 而是直接抛错——调用方都应该在 [Authorize] / API Key 鉴权之后。
/// </summary>
public static class CurrentUser
{
    public static string? Id(ClaimsPrincipal? user)
        => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string RequireId(ClaimsPrincipal? user)
        => Id(user) ?? throw new InvalidOperationException(
            "当前请求没有登录身份。这个操作的调用方应该在 [Authorize] 或 API Key 鉴权之后。");

    public static string? Name(ClaimsPrincipal? user)
        => user?.Identity?.Name;

    public static string? Display(ClaimsPrincipal? user)
        => user?.FindFirst("display")?.Value ?? user?.Identity?.Name;

    public static string Role(ClaimsPrincipal? user)
        => user?.FindFirst(ClaimTypes.Role)?.Value ?? "guest";
}
