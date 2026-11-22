using System.Net;
using System.Text;

namespace Genkan.Network;

/// <summary>
/// 只做一件事：访客用内网 IP 直连访问时，把这个 origin 记进 Cookie。
/// 之后从隧道进来时，布局会读 Cookie 显示「试试直连」按钮——不自动跳转，
/// 因为对方可能根本不在同一个虚拟局域网里，自动跳只会白等。
/// </summary>
public sealed class DirectLinkMiddleware
{
    public const string OriginCookie = "genkan.direct";

    private readonly RequestDelegate _next;
    private readonly bool _enabled;

    public DirectLinkMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _enabled = config.GetValue("network:preferDirect", true);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_enabled &&
            !IsTunnel(context.Request) &&
            HttpMethods.IsGet(context.Request.Method) &&
            IPAddress.TryParse(context.Request.Host.Host, out _))
        {
            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            context.Response.Cookies.Append(
                OriginCookie,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(origin)),
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    Expires = DateTimeOffset.UtcNow.AddDays(30),
                });
        }

        await _next(context);
    }

    public static bool IsTunnel(HttpRequest request)
        => request.Headers.ContainsKey("Cf-Connecting-IP")
           || request.Headers.ContainsKey("CF-Ray")
           || request.Headers.ContainsKey("Cf-Visitor");

    /// <summary>解出记住的直连 origin；没有或不合法就返回 null。</summary>
    public static string? ReadOrigin(HttpRequest request)
    {
        var raw = request.Cookies[OriginCookie];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var origin = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IPAddress.TryParse(uri.Host, out _)
                ? origin.TrimEnd('/')
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
