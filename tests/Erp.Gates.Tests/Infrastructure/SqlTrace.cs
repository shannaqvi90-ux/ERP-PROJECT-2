using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Erp.Kernel.Data;
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
/// <param name="Database">Database the statement went to (environments share one server).</param>
public sealed record TracedCall(string Function, string? Endpoint, string? Path, bool ResolvingSession, object? App, int? Port, string Statement, string? Database = null)
{
    public string Caller => ResolvingSession ? "authentication" : Endpoint is null ? "outside any request" : $"endpoint:{Endpoint}";
}

/// <summary>One binding of a unit of work to a tenant (<see cref="ErpDbSession.BeginAsync"/>),
/// and the request it happened in.</summary>
/// <param name="Tenant">The tenant bound.</param>
/// <param name="PrincipalTenant">The signed-in principal's tenant when the binding happened, or
/// null for an anonymous request (or during the session lookup itself).</param>
public sealed record TracedBind(string Tenant, string ActorKind, string? Endpoint, string? Path, string? Method, bool ResolvingSession,
    string? PrincipalTenant, object? App)
{
    /// <summary>The request's trace identifier.</summary>
    public string? Request { get; init; }

    /// <summary>Order in which the binding started, among all traced kernel session events.</summary>
    public long Sequence { get; init; }

    /// <summary>True when the request only reads (GET, HEAD, ReadOnlyOperation).</summary>
    public bool ReadOnlyRequest { get; init; }

    public string Where => $"{Method} {Path} ({(Endpoint is null ? "no endpoint" : "endpoint:" + Endpoint)})";
}

/// <summary>A statement sent inside a request that changes session or transaction settings (the
/// tenant, the role, row security), with the code that sent it.</summary>
/// <param name="Caller">Outermost product type on the stack when the statement started, or null.</param>
/// <param name="FromSession">True when it ran inside one of the kernel session's own operations.</param>
/// <param name="User">Database user of the connection, when Npgsql reports it.</param>
public sealed record TracedSetting(string Statement, string? Endpoint, string? Path, string? Caller, bool FromSession, string? User, object? App);

/// <summary>A change of a session setting by a statement sent inside a request, with the value it
/// actually sets (read from the statement's parameters) and the request it served.</summary>
/// <param name="Change">The setting, value and scope the statement set.</param>
/// <param name="RequiredTenant">The tenant the request may run under: the signed-in principal's on
/// an endpoint that requires a permission (an empty id for a principal without a tenant), or null
/// for anonymous requests, the session lookup and sign-in.</param>
/// <param name="Caller">Innermost product type on the stack when the statement started.</param>
/// <param name="ProcessId">PostgreSQL backend process the statement ran on.</param>
public sealed record TracedSettingChange(SqlSettings.Change Change, string? Endpoint, string? Path, string? Method, string? Request, string? RequiredTenant,
    bool ResolvingSession, string? Caller, int ProcessId, object? App)
{
    public string Where => $"{Method} {Path} ({(Endpoint is null ? "no endpoint" : "endpoint:" + Endpoint)})";
}

/// <summary>
/// Watches every SQL statement the application sends to PostgreSQL (Npgsql's ActivitySource) and
/// every binding of a unit of work to a tenant (the kernel's <see cref="TenantBinding"/> source).
/// It records:
/// <list type="bullet">
/// <item>statements that name a reviewed security-definer function, with the endpoint and phase
/// that ran them, so the functions that read across tenants run only from their reviewed callers
/// (tests/Gates/security-definer-callers.txt);</item>
/// <item>every tenant binding with the request's signed-in principal, so code that binds any
/// tenant other than the session's (from a header, a route, a body, or a unit of work it built
/// itself) is caught however it found the tenant;</item>
/// <item>every statement inside a request that changes session settings (<c>set_config</c>,
/// <c>SET</c>, <c>RESET</c>, <c>DISCARD</c>), with the code that sent it, so nothing but the
/// kernel's session can change the tenant a statement runs under.</item>
/// </list>
/// </summary>
public static class SqlTrace
{
    private static readonly ConcurrentQueue<TracedCall> Calls = new();
    private static readonly ConcurrentQueue<TracedBind> Binds = new();
    private static readonly ConcurrentQueue<TracedSetting> Settings = new();
    private static readonly ConcurrentQueue<(string Request, long Sequence, object? App)> ReadOnlys = new();
    private static readonly ConcurrentQueue<TracedSettingChange> Changes = new();
    private static readonly ConcurrentQueue<(string Where, string Statement, object? App)> Unobserved = new();
    private static readonly ConcurrentDictionary<object, int> ObservedByApp = new(ReferenceEqualityComparer.Instance);
    private static int _requestStatements;
    private static long _sequence;
    private static readonly HttpContextAccessor Accessor = new();
    private static readonly Lazy<IReadOnlyList<(string Function, string Name)>> Functions = new(() =>
        ReviewedCallers.Read().Select(c => c.Function).Distinct()
            .Select(f => (f, f.Split('.').Last().ToLowerInvariant())).ToList());
    private static readonly Lazy<ActivityListener> Listener = new(() =>
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Npgsql" or TenantBinding.SourceName,
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
        var port = env.DatabasePort;
        return Calls.Skip(since).Where(c => ReferenceEquals(c.App, app) || (c.App is null && c.Port == port && c.Database == env.DatabaseName)).ToList();
    }

    public static int Mark => Calls.Count;

    /// <summary>Positions in every queue, for <see cref="BindsFor"/> and <see cref="SettingsFor"/>.</summary>
    public static TraceMark Snapshot() => new(Calls.Count, Binds.Count, Settings.Count, _requestStatements)
    {
        ReadOnlys = ReadOnlys.Count,
        Changes = Changes.Count,
        Unobserved = Unobserved.Count,
    };

    /// <summary>Setting changes (with the values they set) inside one environment's requests since <paramref name="since"/>.</summary>
    public static IReadOnlyList<TracedSettingChange> ChangesFor(ErpTestEnvironment env, TraceMark since)
    {
        var app = env.Factory.Services.GetService(typeof(ModuleCatalog));
        return Changes.Skip(since.Changes).Where(c => ReferenceEquals(c.App, app)).ToList();
    }

    /// <summary>Statements inside one environment's requests since <paramref name="since"/> that
    /// carried no capture of their parameters: sent on a pool the platform did not build, which
    /// the gate cannot judge.</summary>
    public static IReadOnlyList<string> UnobservedFor(ErpTestEnvironment env, TraceMark since)
    {
        var app = env.Factory.Services.GetService(typeof(ModuleCatalog));
        return Unobserved.Skip(since.Unobserved).Where(u => ReferenceEquals(u.App, app))
            .Select(u => $"{u.Where}: {u.Statement}").Distinct().ToList();
    }

    /// <summary>Statements inside one environment's requests whose parameters the gate captured (all time).</summary>
    public static int ObservedFor(ErpTestEnvironment env) =>
        env.Factory.Services.GetService(typeof(ModuleCatalog)) is { } app && ObservedByApp.TryGetValue(app, out var n) ? n : 0;

    /// <summary>Requests (trace identifier and order) whose transaction was made read-only since <paramref name="since"/>.</summary>
    public static IReadOnlyList<(string Request, long Sequence)> ReadOnlyFor(ErpTestEnvironment env, TraceMark since)
    {
        var app = env.Factory.Services.GetService(typeof(ModuleCatalog));
        return ReadOnlys.Skip(since.ReadOnlys).Where(r => ReferenceEquals(r.App, app)).Select(r => (r.Request, r.Sequence)).ToList();
    }

    /// <summary>Tenant bindings made by one environment's application since <paramref name="since"/>.</summary>
    public static IReadOnlyList<TracedBind> BindsFor(ErpTestEnvironment env, TraceMark since)
    {
        var app = env.Factory.Services.GetService(typeof(ModuleCatalog));
        return Binds.Skip(since.Binds).Where(b => ReferenceEquals(b.App, app)).ToList();
    }

    /// <summary>Setting statements sent inside one environment's requests since <paramref name="since"/>.</summary>
    public static IReadOnlyList<TracedSetting> SettingsFor(ErpTestEnvironment env, TraceMark since)
    {
        var app = env.Factory.Services.GetService(typeof(ModuleCatalog));
        return Settings.Skip(since.Settings).Where(b => ReferenceEquals(b.App, app)).ToList();
    }

    /// <summary>Statements sent inside requests (any environment) since <paramref name="since"/>.</summary>
    public static int RequestStatementsSince(TraceMark since) => _requestStatements - since.RequestStatements;

    private const string CallerProperty = "erp.gate.caller";

    private sealed record Caller(string? Endpoint, string? Path, string? Method, bool ResolvingSession, object? App, string? PrincipalTenant, string? CodeCaller, bool FromSession)
    {
        public string? Request { get; init; }
        public string? RequiredTenant { get; init; }
        public long Sequence { get; init; }
        public bool ReadOnlyRequest { get; init; }
    }

    /// <summary>A statement that changes a session or transaction setting.</summary>
    private static readonly Regex SettingStatement = new(
        @"\bset_config\s*\(|(^|;)\s*(set|reset|discard)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool ChangesSettings(string statement) => SettingStatement.IsMatch(statement);

    private static void Capture(Activity activity)
    {
        var http = Accessor.HttpContext;
        var inSession = false;
        for (var parent = activity.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Source.Name == TenantBinding.SourceName)
            {
                inSession = true;
                break;
            }
        }
        activity.SetCustomProperty(CallerProperty, new Caller(
            http?.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName,
            http?.Request.Path.Value,
            http?.Request.Method,
            SessionResolution.IsInProgress(http),
            http?.RequestServices.GetService(typeof(ModuleCatalog)),
            http?.User.Identity?.IsAuthenticated == true ? http.User.FindTenantId()?.ToString() ?? "" : null,
            // Which code sent a statement is only needed inside requests (the settings rule).
            http is not null && activity.Source.Name == "Npgsql" ? CodeCaller() : null,
            inSession)
        {
            Request = http?.TraceIdentifier,
            RequiredTenant = http is null ? null : TenantBinding.RequiredTenant(http)?.ToString(),
            Sequence = activity.Source.Name == TenantBinding.SourceName ? Interlocked.Increment(ref _sequence) : 0,
            ReadOnlyRequest = http is not null && Erp.Kernel.Http.ReadOnlyRequests.Applies(http),
        });
    }

    /// <summary>The outermost product type on the stack: the class whose method (or async state
    /// machine, lambda or local function) sent the statement.</summary>
    private static string? CodeCaller()
    {
        foreach (var frame in new StackTrace(2, false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            while (type?.DeclaringType is not null)
            {
                type = type.DeclaringType;
            }
            if (type?.Namespace is { } ns && ns.StartsWith("Erp.", StringComparison.Ordinal) && type != typeof(SqlTrace))
            {
                return type.FullName;
            }
        }
        return null;
    }

    private static void Record(Activity activity)
    {
        if (activity.Source.Name == TenantBinding.SourceName)
        {
            if (activity.OperationName == TenantBinding.BindActivity && activity.GetCustomProperty(CallerProperty) is Caller bind && bind.Path is not null)
            {
                Binds.Enqueue(new TracedBind(activity.GetTagItem(TenantBinding.TenantTag) as string ?? "", activity.GetTagItem(TenantBinding.ActorKindTag) as string ?? "",
                    bind.Endpoint, bind.Path, bind.Method, bind.ResolvingSession, bind.PrincipalTenant, bind.App)
                {
                    Request = bind.Request,
                    Sequence = bind.Sequence,
                    ReadOnlyRequest = bind.ReadOnlyRequest,
                });
            }
            else if (activity.OperationName == TenantBinding.ReadOnlyActivity && activity.GetCustomProperty(CallerProperty) is Caller { Request: { } request } readOnly)
            {
                ReadOnlys.Enqueue((request, readOnly.Sequence, readOnly.App));
            }
            return;
        }
        var text = activity.GetTagItem("db.query.text") as string ?? activity.GetTagItem("db.statement") as string;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        if (activity.GetCustomProperty(CallerProperty) is Caller { Path: not null } inRequest)
        {
            Interlocked.Increment(ref _requestStatements);
            if (ChangesSettings(text))
            {
                Settings.Enqueue(new TracedSetting(text, inRequest.Endpoint, inRequest.Path, inRequest.CodeCaller, inRequest.FromSession,
                    UserOf(activity), inRequest.App));
            }
            // The tenant each statement runs under: every setting change is read with the values of
            // the statement's own parameters. A statement without a capture went through a pool the
            // platform did not build, where the gate cannot see its values.
            if (StatementCapture.Of(activity) is { } captured)
            {
                if (inRequest.App is { } observedApp)
                {
                    ObservedByApp.AddOrUpdate(observedApp, 1, (_, n) => n + 1);
                }
                foreach (var command in captured.Commands.Where(c => StatementCapture.NamesSettings(c.Text)))
                {
                    foreach (var change in SqlSettings.Parse(command.Text, command.Parameters))
                    {
                        Changes.Enqueue(new TracedSettingChange(change, inRequest.Endpoint, inRequest.Path, inRequest.Method, inRequest.Request,
                            inRequest.RequiredTenant, inRequest.ResolvingSession, inRequest.CodeCaller, captured.ProcessId, inRequest.App));
                    }
                }
            }
            else
            {
                Unobserved.Enqueue(($"{inRequest.Method} {inRequest.Path}", text.Length <= 160 ? text : text[..160] + "…", inRequest.App));
            }
        }
        var lower = text.ToLowerInvariant();
        foreach (var (function, name) in Functions.Value)
        {
            if (!lower.Contains(name, StringComparison.Ordinal))
            {
                continue;
            }
            var caller = activity.GetCustomProperty(CallerProperty) as Caller ?? new Caller(null, null, null, false, null, null, null, false);
            var port = activity.GetTagItem("server.port") switch
            {
                int p => p,
                long p => (int)p,
                string s when int.TryParse(s, out var p) => p,
                _ => (int?)null,
            };
            Calls.Enqueue(new TracedCall(function, caller.Endpoint, caller.Path, caller.ResolvingSession, caller.App, port, text, activity.GetTagItem("db.namespace") as string));
        }
    }

    /// <summary>The database user of the statement's connection, from the data source name Npgsql
    /// reports (the connection string without its password), when it is there.</summary>
    private static string? UserOf(Activity activity)
    {
        var source = activity.GetTagItem("db.npgsql.data_source") as string;
        if (source is null)
        {
            return null;
        }
        var match = Regex.Match(source, @"(?:^|;)\s*(?:Username|User ID|User Id|UserId|User)\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}

/// <summary>Positions in the trace's queues.</summary>
public sealed record TraceMark(int Calls, int Binds, int Settings, int RequestStatements)
{
    public int ReadOnlys { get; init; }
    public int Changes { get; init; }
    public int Unobserved { get; init; }
}

/// <summary>
/// The rules the trace enforces on tenant binding (G1: the tenant comes only from the session).
/// </summary>
public static class TenantBindingRules
{
    public const string ReviewedFile = "tests/Gates/tenant-binding-endpoints.txt";

    /// <summary>Endpoints reviewed to bind a tenant other than the signed-in principal's (sign-in
    /// binds the tenant of the account whose credentials were given).</summary>
    public static IReadOnlySet<string> ReviewedEndpoints() =>
        Repo.ReadReviewedList(ReviewedFile).Select(e => e.Entry).ToHashSet(StringComparer.Ordinal);

    /// <summary>Bindings inside a request that did not bind the session's tenant: every binding
    /// except the session lookup's own must be the signed-in principal's tenant, unless the
    /// endpoint is reviewed to bind the tenant it resolved itself.</summary>
    public static IReadOnlyList<string> BindViolations(IEnumerable<TracedBind> binds)
    {
        var reviewed = ReviewedEndpoints();
        return binds
            .Where(b => !b.ResolvingSession)
            .Where(b => b.PrincipalTenant != b.Tenant)
            .Where(b => b.Endpoint is null || !reviewed.Contains("endpoint:" + b.Endpoint))
            .Select(b => $"{b.Where} bound tenant {b.Tenant} ({b.ActorKind}) but the request's principal is " +
                         (b.PrincipalTenant is null ? "anonymous" : $"tenant {b.PrincipalTenant}"))
            .Distinct()
            .ToList();
    }

    /// <summary>Bindings in requests that only read (GET, HEAD, ReadOnlyOperation) after which the
    /// transaction was not made read-only: such a request could write.</summary>
    public static IReadOnlyList<string> WritableReads(IEnumerable<TracedBind> binds, IReadOnlyList<(string Request, long Sequence)> readOnlys)
    {
        var byRequest = readOnlys.ToLookup(r => r.Request, r => r.Sequence);
        return binds
            .Where(b => b.ReadOnlyRequest && b.Request is not null)
            .Where(b => !byRequest[b.Request!].Any(sequence => sequence > b.Sequence))
            .Select(b => $"{b.Where} bound tenant {b.Tenant} in a writable transaction although the request only reads")
            .Distinct()
            .ToList();
    }

    /// <summary>Setting statements inside requests sent by anything but the kernel's session, or
    /// to a database user other than the application role.</summary>
    public static IReadOnlyList<string> SettingViolations(IEnumerable<TracedSetting> settings) => settings
        .Where(s => !(s.FromSession || s.Caller == typeof(ErpDbSession).FullName) || (s.User is not null && s.User != DatabaseRoles.App))
        .Select(s => $"{s.Path} ({(s.Endpoint is null ? "no endpoint" : "endpoint:" + s.Endpoint)}): {Short(s.Statement)} sent by {s.Caller ?? "unknown code"}" +
                     (s.User is not null && s.User != DatabaseRoles.App ? $" as database user {s.User}" : ""))
        .Distinct()
        .ToList();

    /// <summary>
    /// The tenant every statement of a request actually runs under, judged by value: a statement
    /// can run under a tenant only if a statement of its transaction set <c>app.tenant_id</c> to it
    /// (a value left on the connection by an earlier transaction, or set for the whole connection,
    /// is ignored by row-level security). So every change of <c>app.tenant_id</c> inside a request
    /// must set the signed-in principal's tenant on an endpoint that requires a permission, and a
    /// tenant the kernel's session declared binding (<see cref="TracedBind"/>, the same request)
    /// anywhere else (sign-in, the session lookup); nothing may change an <c>app.*</c> setting for
    /// the whole connection; and a setting whose name or tenant value the statement computes, where
    /// the gate cannot read it, is refused rather than trusted.
    /// </summary>
    public static IReadOnlyList<string> TenantValueViolations(IEnumerable<TracedSettingChange> changes, IEnumerable<TracedBind> binds)
    {
        var declared = binds.Where(b => b.Request is not null)
            .ToLookup(b => b.Request!, b => b.Tenant, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var c in changes)
        {
            var sent = $"{Short(c.Change.Source)} sent by {c.Caller ?? "unknown code"} on backend {c.ProcessId}";
            if (c.Change.Name is null)
            {
                problems.Add($"{c.Where}: a setting whose name the statement computes ({sent}); the gate cannot tell which setting it changes");
                continue;
            }
            if (!c.Change.Name.StartsWith("app.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (c.Change.Local != true)
            {
                problems.Add($"{c.Where}: {c.Change.Name} set for the whole connection, not the transaction ({sent})");
            }
            if (!c.Change.Name.Equals(SqlSettings.TenantSetting, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (c.Change.Computed)
            {
                problems.Add($"{c.Where}: the tenant is computed by the statement, so the gate cannot read which tenant its SQL runs under ({sent})");
                continue;
            }
            var value = c.Change.Value ?? "";
            if (value.Length == 0)
            {
                continue; // no tenant: row-level security shows nothing (fail closed)
            }
            if (c.RequiredTenant is { } required)
            {
                if (!SameTenant(value, required))
                {
                    problems.Add($"{c.Where}: SQL ran under tenant {value} but the signed-in principal's tenant is {(required == Guid.Empty.ToString() ? "none" : required)} ({sent})");
                }
                continue;
            }
            if (c.Request is null || !declared[c.Request].Any(t => SameTenant(value, t)))
            {
                problems.Add($"{c.Where}: SQL ran under tenant {value}, which the kernel's session never declared binding in this request ({sent})");
            }
        }
        return problems.Distinct().ToList();
    }

    private static bool SameTenant(string value, string tenant) =>
        Guid.TryParse(value, out var a) && Guid.TryParse(tenant, out var b) ? a == b : string.Equals(value, tenant, StringComparison.OrdinalIgnoreCase);

    private static string Short(string statement)
    {
        var flat = Regex.Replace(statement, @"\s+", " ").Trim();
        return flat.Length <= 160 ? flat : flat[..160] + "…";
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
