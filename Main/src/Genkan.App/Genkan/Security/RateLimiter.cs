using System.Collections.Concurrent;

namespace Genkan.Security;

/// <summary>进程内简单限流：按 key（IP / 动作）统计窗口内的次数，重启即清。</summary>
public static class RateLimiter
{
    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> Hits = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryAcquire(string key, int limit, TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow;
        var queue = Hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }

    public static string ClientKey(HttpContext context, string action)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return action + ":" + ip;
    }
}
