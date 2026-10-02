using System.Collections.Concurrent;
using System.Diagnostics;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>One SQL statement that named a reviewed security-definer function, and who ran it.</summary>
/// <param name="Function">The reviewed function (schema.name) the statement names.</param>
/// <param name="Endpoint">Name of the endpoint the request was routed to, or null outside a request.</param>
/// <param name="Path">Request path, or null outside a request.</param>
/// <param name="ResolvingSession">True while the kernel's authentication handler resolved the session.</param>
/// <param name="App">The application (module catalogue) that ran it, null outside a request.</param>
/// <param name="Port">Database port the statement went to.</param>
public sealed record TracedCall(string Function, string? Endpoint, string? Path, bool ResolvingSession, object? App, int? Port, string Statement)
{
    public string Caller => ResolvingSession ? "authentication" : Endpoint is null ? "outside any request" : $"endpoint:{Endpoint}";
}

/// <summary>
/// Watches every SQL statement the application sends to PostgreSQL (Npgsql's ActivitySource) and
/// records those that name a reviewed security-definer function, with the endpoint and phase that
/// ran them. The tenant-isolation gate uses it to prove that the functions that read across
/// tenants run only from their reviewed callers (tests/Gates/security-definer-callers.txt), so a
/// new endpoint cannot quietly reuse them as a cross-tenant lookup.
/// </summary>
public static class SqlTrace
{
    private static readonly ConcurrentQueue<TracedCall> Calls = new();
    private static readonly HttpContextAccessor Accessor = new();
    private static readonly Lazy<IReadOnlyList<(string Function, string Name)>> Functions = new(() =>
        ReviewedCallers.Read().Select(c => c.Function).Distinct()
            .Select(f => (f, f.Split('.').Last().ToLowerInvariant())).ToList());
    private static readonly Lazy<ActivityListener> Listener = new(() =>
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            // Npgsql adds the statement text after the activity starts, so the caller is captured at
            // start (same async flow as the command) and matched with the text at stop.
            ActivityStarted = Capture,
            ActivityStopped = Record,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    });

    /// <summary>Start listening (idempotent). Must run before the traffic to be judged.</summary>
    public static void EnsureStarted() => _ = Listener.Value;

    /// <summary>Calls made by one environment's application since <paramref name="since"/>.</summary>
    public static IReadOnlyList<TracedCall> For(ErpTestEnvironment env, int since = 0)
    {
        var app = env.Factory.Services.GetService(typeof(ModuleCatalog));
        var port = env.Container.GetMappedPublicPort(5432);
        return Calls.Skip(since).Where(c => ReferenceEquals(c.App, app) || (c.App is null && c.Port == port)).ToList();
    }

    public static int Mark => Calls.Count;

    private const string CallerProperty = "erp.gate.caller";

    private sealed record Caller(string? Endpoint, string? Path, bool ResolvingSession, object? App);

    private static void Capture(Activity activity)
    {
        var http = Accessor.HttpContext;
        activity.SetCustomProperty(CallerProperty, new Caller(
            http?.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
            http?.Request.Path.Value,
            SessionResolution.IsInProgress(http),
            http?.RequestServices.GetService(typeof(ModuleCatalog))));
    }

    private static void Record(Activity activity)
    {
        var text = activity.GetTagItem("db.query.text") as string ?? activity.GetTagItem("db.statement") as string;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        var lower = text.ToLowerInvariant();
        foreach (var (function, name) in Functions.Value)
        {
            if (!lower.Contains(name, StringComparison.Ordinal))
            {
                continue;
            }
            var caller = activity.GetCustomProperty(CallerProperty) as Caller ?? new Caller(null, null, false, null);
            var port = activity.GetTagItem("server.port") switch
            {
                int p => p,
                long p => (int)p,
                string s when int.TryParse(s, out var p) => p,
                _ => (int?)null,
            };
            Calls.Enqueue(new TracedCall(function, caller.Endpoint, caller.Path, caller.ResolvingSession, caller.App, port, text));
        }
    }
}

/// <summary>tests/Gates/security-definer-callers.txt: who may run each reviewed function, and
/// which source files may name it.</summary>
public static class ReviewedCallers
{
    public const string File = "tests/Gates/security-definer-callers.txt";

    public sealed record Entry(string Function, string Kind, string Value, string Reason);

    public static IReadOnlyList<Entry> Read() =>
        Repo.ReadReviewedList(File).Select(line =>
        {
            var parts = line.Entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[1] is not ("caller" or "source"))
            {
                throw new InvalidOperationException($"{File}: '{line.Entry}' must be 'schema.function caller|source value'.");
            }
            return new Entry(parts[0], parts[1], parts[2], line.Reason);
        }).ToList();

    /// <summary>Calls whose caller is not reviewed for that function.</summary>
    public static IReadOnlyList<string> Misuse(IEnumerable<TracedCall> calls)
    {
        var allowed = Read().Where(e => e.Kind == "caller").ToLookup(e => e.Function, e => e.Value);
        return calls.Where(c => !allowed[c.Function].Contains(c.Caller))
            .Select(c => $"{c.Function} ran from {c.Caller}{(c.Path is null ? "" : $" ({c.Path})")}")
            .Distinct()
            .ToList();
    }
}
