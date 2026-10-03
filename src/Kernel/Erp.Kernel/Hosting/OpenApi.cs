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

internal static class OpenApiSetup
{
    /// <summary>OpenAPI document generated from the running app: every endpoint, its permission
    /// (<c>x-erp-permission</c>) or anonymous reason (<c>x-erp-anonymous</c>), decimals as strings.</summary>
    public static IServiceCollection AddErpOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
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
