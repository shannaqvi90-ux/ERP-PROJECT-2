using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Erp.Kernel.Http;

/// <summary>
/// Which partition of a rate-limited endpoint's limit a request counts against, when the module
/// can tell more than its network address does (decision p03-identity-shared-address-rate-limit).
/// <c>Key</c>: the partition of the endpoint's own limit (a client's budget). <c>SharedCeiling</c>:
/// when the address is one many clients share, the address itself, whose clients together get
/// <c>Erp:RateLimits:SharedAddressClients</c> clients' budget; null for an address of one client.
/// </summary>
public sealed record RateLimitClient(string Key, string? SharedCeiling);

/// <summary>
/// Endpoint metadata: the partition a request of this endpoint counts against, worked out before
/// the rate limiter runs (it may read the request body, which it leaves readable for the
/// endpoint). A null answer keeps the default partition, the remote address.
/// </summary>
public sealed class RateLimitClientMetadata(Func<HttpContext, ValueTask<RateLimitClient?>> resolve)
{
    public Func<HttpContext, ValueTask<RateLimitClient?>> Resolve { get; } = resolve;
}

public static class RateLimitClientExtensions
{
    /// <summary>Where the partition worked out for the request is kept for the limiter.</summary>
    public const string PartitionItem = "erp.rateLimitPartition";

    /// <summary>Count this endpoint's requests against the partition <paramref name="resolve"/>
    /// names (see <see cref="RateLimitClientMetadata"/>).</summary>
    public static TBuilder WithRateLimitClient<TBuilder>(this TBuilder builder, Func<HttpContext, ValueTask<RateLimitClient?>> resolve)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RateLimitClientMetadata(resolve));

    /// <summary>The partition worked out for this request, or null (the remote address).</summary>
    public static RateLimitClient? RateLimitClientOf(this HttpContext context) =>
        context.Items.TryGetValue(PartitionItem, out var value) ? value as RateLimitClient : null;
}
