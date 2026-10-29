using System.Net;
using System.Threading.RateLimiting;

namespace MovieAgentCLI.Handlers;

public class RateLimitingHandler : DelegatingHandler
{
    private readonly TokenBucketRateLimiter _rateLimiter;

    public RateLimitingHandler()
    {
        _rateLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 10,
            TokensPerPeriod = 10,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 5,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await _rateLimiter.AcquireAsync(
            permitCount: 1,
            cancellationToken: cancellationToken);

        if (!lease.IsAcquired)
        {
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("Rate limit exceeded. Try again later.")
            };
        }

        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        _rateLimiter.Dispose();
        base.Dispose(disposing);
    }
}
