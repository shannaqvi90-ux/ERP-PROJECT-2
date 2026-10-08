using Erp.Kernel.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Http;

/// <summary>One field error: a stable code clients can map, and a localized message.</summary>
public sealed record FieldError(string Code, string Message);

/// <summary>
/// RFC 9457 problem responses with a stable machine code (<c>code</c>) and a title in the
/// request's language (the user's preference, else Accept-Language).
/// </summary>
public static class Problems
{
    public static ProblemDetails Create(HttpContext context, int status, string code, IDictionary<string, FieldError[]>? errors = null, params object?[] args)
    {
        var strings = context.RequestServices.GetService<StringCatalog>();
        var language = Languages.ForRequest(context);
        var problem = new ProblemDetails
        {
            Status = status,
            Type = $"urn:erp:problem:{code}",
            Title = strings?.Get($"problem.{code}", language, args) ?? code,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }
        return problem;
    }

    public static ProblemHttpResult Result(HttpContext context, int status, string code, IDictionary<string, FieldError[]>? errors = null, params object?[] args)
    {
        var problem = Create(context, status, code, errors, args);
        return TypedResults.Problem(problem);
    }

    public static Task Write(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Create(context, status, code), options: null, contentType: "application/problem+json");
    }

    public static ProblemHttpResult NotFound(HttpContext context) => Result(context, StatusCodes.Status404NotFound, "notFound");

    public static ProblemHttpResult Forbidden(HttpContext context, string code = "auth.forbidden") => Result(context, StatusCodes.Status403Forbidden, code);

    public static ProblemHttpResult Conflict(HttpContext context, string code) => Result(context, StatusCodes.Status409Conflict, code);
}

/// <summary>Collects field errors with localized messages. Field names are the JSON property
/// names (camelCase).</summary>
public sealed class Validator(HttpContext context)
{
    private readonly Dictionary<string, List<FieldError>> _errors = new(StringComparer.Ordinal);
    private readonly StringCatalog _strings = context.RequestServices.GetRequiredService<StringCatalog>();
    private readonly string _language = Languages.ForRequest(context);

    public bool IsValid => _errors.Count == 0;

    public Validator Add(string field, string code, params object?[] args)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            _errors[field] = list = [];
        }
        list.Add(new FieldError(code, _strings.Get($"validation.{code}", _language, args)));
        return this;
    }

    public Validator Required(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, "required");
        }
        return this;
    }

    public Validator Required<T>(string field, T? value) where T : struct
    {
        if (value is null)
        {
            Add(field, "required");
        }
        return this;
    }

    public Validator MaxLength(string field, string? value, int max)
    {
        if (value is not null && value.Length > max)
        {
            Add(field, "maxLength", max);
        }
        return this;
    }

    public Validator Length(string field, string? value, int min, int max)
    {
        if (value is not null && (value.Length < min || value.Length > max))
        {
            Add(field, "length", min, max);
        }
        return this;
    }

    public Validator Email(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !IsEmail(value))
        {
            Add(field, "email");
        }
        return this;
    }

    public Validator OneOf(string field, string? value, IReadOnlyCollection<string> allowed)
    {
        if (value is not null && !allowed.Contains(value))
        {
            Add(field, "oneOf", string.Join(", ", allowed));
        }
        return this;
    }

    public Validator Must(bool condition, string field, string code, params object?[] args)
    {
        if (!condition)
        {
            Add(field, code, args);
        }
        return this;
    }

    public ProblemHttpResult ToResult() => Problems.Result(context, StatusCodes.Status400BadRequest, "validation",
        _errors.ToDictionary(p => p.Key, p => p.Value.ToArray()));

    public static bool IsEmail(string value)
    {
        if (value.Length > 254 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }
        var domain = value[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..");
    }
}
