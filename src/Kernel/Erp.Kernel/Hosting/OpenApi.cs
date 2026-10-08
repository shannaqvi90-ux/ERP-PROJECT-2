using System.Text.Json.Nodes;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Erp.Kernel.Hosting;

/// <summary>An example value for a request or response property, shown in the API description
/// (<c>example</c>). Give one wherever a field has a format a client cannot guess (codes, patterns),
/// so the documented example is a valid value.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class ApiExampleAttribute(string value) : Attribute
{
    public string Value { get; } = value;
}

/// <summary>
/// The API description, generated once, on the first call, and served as the same text to every
/// caller after that. Built per request (as <c>MapOpenApi</c> does), every call of this anonymous
/// endpoint rebuilt the whole document from every endpoint's metadata: about 300 ms of processor
/// time each, which anyone could ask for again and again. The routes and shapes it describes are
/// fixed once the app serves requests, so nothing is lost by building it once; it holds routes and
/// shapes only, never data, and is the same for every tenant. It is not built while the host starts:
/// the framework's endpoint description is read once and kept, and at start-up it can still miss
/// endpoints (the gates saw that). No <c>servers</c> entry is written (a URL taken from a request's
/// Host header would differ by caller); clients use the address they fetched it from.
/// </summary>
internal sealed class OpenApiDescription
{
    public const string DocumentName = "v1";

    /// <summary>Made by dependency injection the first time the endpoint is called (a singleton:
    /// one instance, built once, under the container's own lock).</summary>
    public OpenApiDescription(IServiceProvider services)
    {
        var provider = services.GetRequiredKeyedService<Microsoft.AspNetCore.OpenApi.IOpenApiDocumentProvider>(DocumentName);
        var document = provider.GetOpenApiDocumentAsync(CancellationToken.None).GetAwaiter().GetResult();
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        document.SerializeAsV31(new Microsoft.OpenApi.OpenApiJsonWriter(text));
        Json = text.ToString();
    }

    /// <summary>The OpenAPI 3.1 document as JSON.</summary>
    public string Json { get; }
}

internal static class OpenApiSetup
{
    /// <summary>OpenAPI document generated from the running app: every endpoint, its permission
    /// (<c>x-erp-permission</c>) or anonymous reason (<c>x-erp-anonymous</c>), decimals as strings.</summary>
    public static IServiceCollection AddErpOpenApi(this IServiceCollection services)
    {
        services.AddSingleton<OpenApiDescription>();
        services.AddOpenApi(OpenApiDescription.DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "ERP platform API",
                    Version = "v1",
                    Description = "Every action the screens perform. Sign in with POST /api/auth/sign-in " +
                                  "(cookie, or set issueToken for a bearer token). Unsafe requests made with the " +
                                  "cookie must send the header X-Erp-Request: 1. Each operation lists the permission " +
                                  "it requires in x-erp-permission. Decimal values are JSON strings.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["session"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Cookie,
                    Name = SessionAuthenticationDefaults.CookieName,
                    Description = "Session cookie set by POST /api/auth/sign-in.",
                };
                document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "Session token returned by POST /api/auth/sign-in with issueToken: true.",
                };
                return Task.CompletedTask;
            });

            options.AddSchemaTransformer((schema, context, _) =>
            {
                var type = context.JsonTypeInfo.Type;
                if (context.JsonPropertyInfo?.AttributeProvider?.GetCustomAttributes(typeof(ApiExampleAttribute), true)
                        .OfType<ApiExampleAttribute>().FirstOrDefault() is { } example)
                {
                    schema.Examples = [JsonValue.Create(example.Value)];
                }
                if (type == typeof(decimal) || type == typeof(decimal?))
                {
                    schema.Type = type == typeof(decimal?) ? JsonSchemaType.String | JsonSchemaType.Null : JsonSchemaType.String;
                    schema.Format = "decimal";
                    schema.Pattern = "^-?[0-9]+(\\.[0-9]+)?$";
                }
                if (context.JsonPropertyInfo?.AttributeProvider?.GetCustomAttributes(typeof(Erp.Kernel.Http.AllowedTextValuesAttribute), true)
                        .OfType<Erp.Kernel.Http.AllowedTextValuesAttribute>().FirstOrDefault() is { } allowed)
                {
                    // A field that may be left out (null) keeps null among its values.
                    var values = allowed.Values.Select(v => (JsonNode?)JsonValue.Create(v)).ToList();
                    if (schema.Type is { } t && t.HasFlag(JsonSchemaType.Null))
                    {
                        values.Add(null);
                    }
                    schema.Enum = values!;
                }
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var permission = metadata.OfType<RequiresPermissionAttribute>().FirstOrDefault();
                var anonymous = metadata.OfType<AnonymousReasonAttribute>().FirstOrDefault();
                operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                if (permission is not null)
                {
                    operation.Extensions["x-erp-permission"] = new JsonNodeExtension(JsonValue.Create(permission.Permission));
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("session", context.Document)] = [],
                        [new OpenApiSecuritySchemeReference("bearer", context.Document)] = [],
                    });
                }
                if (anonymous is not null)
                {
                    operation.Extensions["x-erp-anonymous"] = new JsonNodeExtension(JsonValue.Create(anonymous.Reason));
                }
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
