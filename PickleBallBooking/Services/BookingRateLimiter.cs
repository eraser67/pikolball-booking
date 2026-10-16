using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace PickleBallBooking.Services;

public class BookingRateLimiter : IBookingRateLimiter
{
    private readonly TimeSpan _window;
    private readonly int _maxBookings;
    private readonly ConcurrentDictionary<string, List<DateTime>> _attempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cleanupLock = new();
    private DateTime _lastCleanupUtc = DateTime.UtcNow;

    public BookingRateLimiter(int maxBookings = 3, TimeSpan? window = null)
    {
        _maxBookings = maxBookings;
        _window = window ?? TimeSpan.FromMinutes(10);
    }

    public bool IsAllowed(string ipAddress, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return true;
        }

        MaybeCleanup();

        var now = DateTime.UtcNow;
        var cutoff = now - _window;

        if (_attempts.TryGetValue(ipAddress, out var list))
        {
            lock (list)
            {
                list.RemoveAll(t => t <= cutoff);
                if (list.Count >= _maxBookings)
                {
                    var oldest = list[0];
                    var expiresAt = oldest + _window;
                    retryAfter = expiresAt > now ? expiresAt - now : TimeSpan.FromSeconds(1);
                    return false;
                }
            }
        }

        return true;
    }

    public void RecordAttempt(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var cutoff = now - _window;

        var list = _attempts.GetOrAdd(ipAddress, _ => new List<DateTime>());
        lock (list)
        {
            list.RemoveAll(t => t <= cutoff);
            list.Add(now);
        }
    }

    public string GetClientIp(HttpContext httpContext)
    {
        if (httpContext == null) return "unknown";

        // Check standard reverse proxy headers (Nginx, Cloudflare, AWS ALB)
        var xff = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(xff))
        {
            var firstIp = xff.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstIp))
            {
                return firstIp;
            }
        }

        var xRealIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(xRealIp))
        {
            return xRealIp.Trim();
        }

        return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void MaybeCleanup()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanupUtc < TimeSpan.FromMinutes(5))
        {
            return;
        }

        lock (_cleanupLock)
        {
            if (now - _lastCleanupUtc < TimeSpan.FromMinutes(5))
            {
                return;
            }
            _lastCleanupUtc = now;

            var cutoff = now - _window;
            foreach (var kvp in _attempts)
            {
                lock (kvp.Value)
                {
                    kvp.Value.RemoveAll(t => t <= cutoff);
                }
                if (kvp.Value.Count == 0)
                {
                    _attempts.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
