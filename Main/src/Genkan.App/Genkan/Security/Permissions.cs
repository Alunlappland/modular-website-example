using System.Security.Claims;

namespace Genkan.Security;

/// <summary>
/// 角色 → 权限点。guest 只有博客只读，member 有日常能力，admin 全量。
/// 模块声明权限点（extend.json / 菜单 / 路由），统一在这里判定。
/// </summary>
public static class Permissions
{
    public static readonly IReadOnlyDictionary<string, string[]> Roles =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin"] = new[] { "*" },
            ["member"] = new[]
            {
                "chat.use",
                "blog.read",
                "files.read",
                "files.write",
                "sync.use",
                "backup.view",
            },
            ["guest"] = new[] { "blog.read" },
        };

    public static bool Has(ClaimsPrincipal? user, string permission)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            // 匿名访客按 guest 角色处理（博客只读等公开端点自行判断）
            return Roles["guest"].Contains(permission, StringComparer.OrdinalIgnoreCase);
        }

        if (user.IsInRole("admin"))
        {
            return true;
        }

        foreach (var (role, granted) in Roles)
        {
            if (user.IsInRole(role) &&
                granted.Contains(permission, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAuthenticated(ClaimsPrincipal? user)
        => user?.Identity?.IsAuthenticated == true;
}
