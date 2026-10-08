using System.Text;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Erp.Kernel.Hosting;

/// <summary>
/// The OpenAPI document of the running app (GET /api/openapi/v1.json), generated once when it is
/// first needed and served as is afterwards. It depends only on the code (routes, shapes,
/// permissions; no server URL, see <see cref="OpenApiSetup"/>), so every caller gets the same text.
/// The framework's own endpoint generated it on every request, which cost about ten ordinary
/// requests of processor time each.
/// </summary>
internal sealed class ApiDescriptionDocument
{
    /// <summary>The document's name (its path segment).</summary>
    public const string Name = "v1";

    public ApiDescriptionDocument([FromKeyedServices(Name)] IOpenApiDocumentProvider provider)
    {
        // Built once, when the singleton is first resolved (normally by the first request for
        // the document); a constructor cannot await.
        var document = provider.GetOpenApiDocumentAsync().GetAwaiter().GetResult();
        using var stream = new MemoryStream();
        document.SerializeAsJsonAsync(stream, OpenApiSpecVersion.OpenApi3_1).GetAwaiter().GetResult();
        Json = Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The document as OpenAPI 3.1 JSON.</summary>
    public string Json { get; }
}
