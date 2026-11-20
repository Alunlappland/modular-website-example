using System.Net;

namespace Genkan.Security;

/// <summary>
/// 「仅本机」判定。隧道过来的请求虽然源地址也是 127.0.0.1，
/// 但会带 Cloudflare 头，所以必须靠头来区分。
/// </summary>
public static class LocalOnly
{
    public static bool IsLocal(HttpContext context)
    {
        var headers = context.Request.Headers;

        if (headers.ContainsKey("Cf-Connecting-IP") ||
            headers.ContainsKey("CF-Ray") ||
            headers.ContainsKey("Cf-Visitor"))
        {
            return false;
        }

        var remote = context.Connection.RemoteIpAddress;
        return remote is not null && IPAddress.IsLoopback(remote);
    }
}
