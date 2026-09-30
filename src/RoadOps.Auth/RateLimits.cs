using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RoadOps.Auth;

/// <summary>Requests allowed per minute (fixed window), configured under "RateLimits". 0 or less means no limit.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Requests per signed-in user that no stricter bucket matches.</summary>
    public int RequestsPerMinute { get; set; } = 120;

    /// <summary>Requests without a valid key, per client IP: slows down key guessing.</summary>
    public int AnonymousRequestsPerMinute { get; set; } = 30;
}

/// <summary>
/// A stricter per-user limit for expensive requests (e.g. uploads or writes). A request counts only against the first
/// bucket it matches, not also against <see cref="RateLimitOptions.RequestsPerMinute"/>.
/// </summary>
public sealed record RateLimitBucket(string Name, Func<HttpRequest, bool> Matches, int PermitsPerMinute);

/// <summary>
/// Per-caller rate limits for both hosts. The middleware must run after authentication (<c>UseRateLimiter()</c> between
/// <c>UseAuthentication()</c> and <c>UseAuthorization()</c>), so a signed-in user is limited by user name (one runaway
/// client cannot starve the others) and everyone else by IP address. Rejections are 429 with Retry-After.
/// </summary>
public static class RateLimits
{
    public static IServiceCollection AddRoadOpsRateLimits(
        this IServiceCollection services, IConfiguration configuration, params RateLimitBucket[] buckets)
    {
        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (http.Request.Path.StartsWithSegments("/health"))
                {
                    return RateLimitPartition.GetNoLimiter("health");
                }

                var user = http.User.Identity is { IsAuthenticated: true } identity ? identity.Name : null;
                if (user is null)
                {
                    return PerMinute($"ip:{http.Connection.RemoteIpAddress}", limits.AnonymousRequestsPerMinute);
                }

                var bucket = buckets.FirstOrDefault(b => b.Matches(http.Request));
                return bucket is null
                    ? PerMinute($"user:{user}", limits.RequestsPerMinute)
                    : PerMinute($"{bucket.Name}:{user}", bucket.PermitsPerMinute);
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RateLimits))
                    .LogWarning("Rate limit hit by {Caller} on {Method} {Path}.",
                        http.User.Identity?.Name ?? http.Connection.RemoteIpAddress?.ToString(), http.Request.Method, http.Request.Path);
                await http.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Wait a moment and try again.",
                    },
                    options: null, contentType: "application/problem+json", cancellationToken);
            };
        });
    }

    private static RateLimitPartition<string> PerMinute(string key, int permits) =>
        permits <= 0
            ? RateLimitPartition.GetNoLimiter(key)
            : RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
}
