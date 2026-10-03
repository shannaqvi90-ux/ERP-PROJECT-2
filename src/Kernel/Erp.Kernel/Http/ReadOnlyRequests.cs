using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Erp.Kernel.Http;

/// <summary>Endpoint metadata: a POST (or other method with a body) that only reads, such as a
/// search whose criteria are too large for a query string. It runs in a read-only transaction,
/// like every GET, so PostgreSQL refuses any write it attempts; that is what allows such an
/// endpoint to declare a read permission.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ReadOnlyOperationAttribute : Attribute;

public static class ReadOnlyEndpointExtensions
{
    /// <summary>Mark an endpoint that changes nothing (see <see cref="ReadOnlyOperationAttribute"/>).</summary>
    public static TBuilder ReadOnlyOperation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new ReadOnlyOperationAttribute());
        return builder;
    }
}

/// <summary>Which requests run in a read-only transaction: every GET and HEAD (safe methods,
/// which the CSRF defence lets through without its header, so they must never write) and every
/// endpoint marked <see cref="ReadOnlyOperationAttribute"/>.</summary>
public static class ReadOnlyRequests
{
    public static bool Applies(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
        context.GetEndpoint()?.Metadata.GetMetadata<ReadOnlyOperationAttribute>() is not null;
}
