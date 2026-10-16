using Microsoft.AspNetCore.Http;

namespace PickleBallBooking.Services;

public interface IBookingRateLimiter
{
    /// <summary>
    /// Checks whether the IP address has exceeded the booking rate limit.
    /// </summary>
    /// <param name="ipAddress">The client's IP address.</param>
    /// <param name="retryAfter">The remaining duration before the client can attempt another booking.</param>
    /// <returns>True if allowed; false if rate-limited.</returns>
    bool IsAllowed(string ipAddress, out TimeSpan retryAfter);

    /// <summary>
    /// Records a confirmed booking attempt for the given IP address.
    /// </summary>
    /// <param name="ipAddress">The client's IP address.</param>
    void RecordAttempt(string ipAddress);

    /// <summary>
    /// Extracts the client's real IP address from HttpContext,
    /// respecting X-Forwarded-For or X-Real-IP headers if behind a reverse proxy.
    /// </summary>
    string GetClientIp(HttpContext httpContext);
}
