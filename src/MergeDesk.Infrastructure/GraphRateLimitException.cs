using System.Net;

namespace MergeDesk.Infrastructure;

public sealed class GraphRateLimitException(string message, DateTimeOffset retryAfter)
    : HttpRequestException(message, null, HttpStatusCode.TooManyRequests)
{ public DateTimeOffset RetryAfter { get; } = retryAfter; }
