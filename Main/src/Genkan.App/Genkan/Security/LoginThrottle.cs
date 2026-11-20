using System.Collections.Concurrent;

namespace Genkan.Security;

/// <summary>登录失败节流：同一 IP+用户名 5 次失败锁 5 分钟（进程内，重启即清）。</summary>
public static class LoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, Entry> Map = new(StringComparer.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset WindowStart;
        public DateTimeOffset LockedUntil;
    }

    public static bool IsLocked(string key, out int retrySeconds)
    {
        retrySeconds = 0;
        if (!Map.TryGetValue(key, out var entry))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (entry.LockedUntil > now)
        {
            retrySeconds = (int)Math.Ceiling((entry.LockedUntil - now).TotalSeconds);
            return true;
        }

        return false;
    }

    public static void Fail(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = Map.GetOrAdd(key, _ => new Entry { WindowStart = now });

        lock (entry)
        {
            if (now - entry.WindowStart > Window)
            {
                entry.Failures = 0;
                entry.WindowStart = now;
            }

            entry.Failures++;
            if (entry.Failures >= MaxFailures)
            {
                entry.LockedUntil = now.Add(Window);
                entry.Failures = 0;
            }
        }
    }

    public static void Reset(string key) => Map.TryRemove(key, out _);
}
