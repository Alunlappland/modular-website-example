using Microsoft.AspNetCore.DataProtection;

namespace Genkan.Security;

/// <summary>
/// 临时提权：成功后写一个 15 分钟有效的加密 Cookie。
/// 敏感操作（删快照、恢复、导出恢复包）要求提权，避免一次误点。
/// </summary>
public sealed class ElevationService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly IDataProtector _protector;

    public ElevationService(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("genkan.elevation.v1");

    private const string CookieName = "genkan.elev";

    public bool IsElevated(HttpContext context, string userId)
    {
        var raw = context.Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var payload = _protector.Unprotect(raw);
            var parts = payload.Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[1], out var expiry))
            {
                return false;
            }

            return string.Equals(parts[0], userId, StringComparison.Ordinal)
                   && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiry;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public DateTimeOffset Elevate(HttpContext context, string userId)
    {
        var expires = DateTimeOffset.UtcNow.Add(Lifetime);
        var payload = _protector.Protect($"{userId}|{expires.ToUnixTimeSeconds()}");

        context.Response.Cookies.Append(CookieName, payload, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Expires = expires,
        });

        return expires;
    }

    public void Drop(HttpContext context) => context.Response.Cookies.Delete(CookieName);
}
