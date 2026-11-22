using System.Net;
using System.Text;

namespace Genkan.Network;

/// <summary>
/// 「同网络就直连」：访客用内网地址访问过一次后，记下这个地址；
/// 之后他从 Cloudflare 域名进来，就直接 302 到内网地址，省掉隧道那一段。
/// 防死循环：跳转后 60 秒内又走隧道回来，就认为直连不通，忘掉并停止跳转。
/// </summary>
public sealed class DirectLinkMiddleware
{
    private const string OriginCookie = "genkan.direct";
    private const string AttemptCookie = "genkan.direct.try";

    private static readonly string[] SkipPrefixes =
    {
        "/api", "/assets", "/healthz", "/theme", "/favicon", "/chat", "/admin",
    };

    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private readonly ILogger<DirectLinkMiddleware> _logger;

    public DirectLinkMiddleware(RequestDelegate next, IConfiguration config, ILogger<DirectLinkMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _enabled = config.GetValue("network:preferDirect", true);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        var isTunnel = IsTunnel(context.Request);
        var path = context.Request.Path.Value ?? "/";

        if (!isTunnel)
        {
            // 直连访问：如果是 IP 地址，就把这个 origin 记下来
            if (HttpMethods.IsGet(context.Request.Method) &&
                IPAddress.TryParse(context.Request.Host.Host, out _))
            {
                var originUrl = $"{context.Request.Scheme}://{context.Request.Host}";
                context.Response.Cookies.Append(OriginCookie, Convert.ToBase64String(Encoding.UTF8.GetBytes(originUrl)),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = context.Request.IsHttps,
                        Expires = DateTimeOffset.UtcNow.AddDays(30),
                    });
            }

            await _next(context);
            return;
        }

        // 隧道访问：只在 GET 页面时考虑跳转
        var canRedirect = HttpMethods.IsGet(context.Request.Method)
                          && !context.Request.Query.ContainsKey("direct")
                          && !SkipPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                          && (context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)
                              || string.IsNullOrEmpty(context.Request.Headers.Accept));

        if (!canRedirect)
        {
            await _next(context);
            return;
        }

        // 上次跳过去又回来了，说明直连不通：忘掉，别再跳
        if (context.Request.Cookies.ContainsKey(AttemptCookie))
        {
            context.Response.Cookies.Delete(OriginCookie);
            context.Response.Cookies.Delete(AttemptCookie);
            _logger.LogInformation("直连尝试失败，暂时回退到隧道访问");
            await _next(context);
            return;
        }

        var saved = context.Request.Cookies[OriginCookie];
        if (string.IsNullOrWhiteSpace(saved))
        {
            await _next(context);
            return;
        }

        string origin;
        try
        {
            origin = Encoding.UTF8.GetString(Convert.FromBase64String(saved));
        }
        catch (FormatException)
        {
            context.Response.Cookies.Delete(OriginCookie);
            await _next(context);
            return;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !IPAddress.TryParse(uri.Host, out _))
        {
            context.Response.Cookies.Delete(OriginCookie);
            await _next(context);
            return;
        }

        var target = origin.TrimEnd('/') + context.Request.Path + context.Request.QueryString +
                     (context.Request.QueryString.HasValue ? "&" : "?") + "direct=0";

        context.Response.Cookies.Append(AttemptCookie, "1", new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddSeconds(60),
        });

        _logger.LogInformation("访客可直连，重定向到 {Target}", target);
        context.Response.Redirect(target);
    }

    private static bool IsTunnel(HttpRequest request)
        => request.Headers.ContainsKey("Cf-Connecting-IP")
           || request.Headers.ContainsKey("CF-Ray")
           || request.Headers.ContainsKey("Cf-Visitor");
}
