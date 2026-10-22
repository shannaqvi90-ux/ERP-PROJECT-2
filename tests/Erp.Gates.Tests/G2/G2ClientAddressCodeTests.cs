using System.Reflection;
using System.Reflection.Emit;
using Erp.Gates.Tests.G1;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, compiled code: no permission decision depends on where a request comes from (critic p00
/// round 13, plant P11: the kernel's permission handler passed every signed-in request from
/// loopback). The client-origin matrices (<see cref="G2ClientOriginTests"/>) try the decisions from
/// fifteen addresses; this gate reads the product's compiled code. Every method that reads the
/// request's network origin (the connection's addresses, ports or client certificate, the
/// connection feature, the forwarded-header machinery, or a forwarding header named in a string)
/// is reviewed one by one in tests/Gates/client-address-readers.txt, and none may be reachable from
/// the authentication and authorization code (every product type that is an authorization handler,
/// requirement, policy provider or evaluator, an authentication handler or a claims
/// transformation), following calls into product code, interface and virtual dispatch to every
/// product implementation, lambdas, local functions and async state machines.
/// </summary>
public sealed class G2ClientAddressCodeTests
{
    public const string ReviewedFile = "tests/Gates/client-address-readers.txt";

    [Fact]
    public void Only_reviewed_methods_read_where_a_request_comes_from_and_no_permission_decision_reaches_one()
    {
        var found = ClientAddressCode.Scan(TenantSettingCode.ProductAssemblies());
        var problems = ClientAddressCode.Check(found, Repo.ReadReviewedList(ReviewedFile));
        TestContext.Current.TestOutputHelper?.WriteLine($"{found.MethodsScanned} methods scanned, {found.Roots.Count} on the authentication and authorization path, {found.Reached.Count} reached from it; readers:");
        foreach (var (method, uses) in found.Readers.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"  {method}: {string.Join(", ", uses)}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        // The kernel's own permission handler and session authentication are on the path.
        Assert.Contains(found.Roots, r => r.StartsWith(typeof(PermissionRequirement).Namespace + ".PermissionAuthorizationHandler.", StringComparison.Ordinal));
        Assert.Contains(found.Roots, r => r.StartsWith(typeof(PermissionRequirement).Namespace + ".SessionAuthenticationHandler.", StringComparison.Ordinal));
        Assert.True(found.Reached.Count >= Ratchet.Min("g2.authorizationPathMethods"),
            $"{found.Reached.Count} methods on the authorization path; ratchet minimum {Ratchet.Min("g2.authorizationPathMethods")}");
        Assert.True(found.MethodsScanned >= Ratchet.Min("g1.outsideProcessMethodsScanned"),
            $"{found.MethodsScanned} methods scanned; ratchet minimum {Ratchet.Min("g1.outsideProcessMethodsScanned")}");
    }

    [Fact]
    public void The_scan_catches_a_permission_check_that_reads_the_client_address_directly_through_a_helper_a_feature_or_an_interface()
    {
        var found = ClientAddressCode.Scan([typeof(PlantedAddressChecks).Assembly], t => t == typeof(PlantedAddressChecks) || IsPlanted(t));
        static bool IsPlanted(Type t)
        {
            for (var x = t; x is not null; x = x.DeclaringType)
            {
                if (x == typeof(PlantedAddressChecks))
                {
                    return true;
                }
            }
            return false;
        }
        var owner = typeof(PlantedAddressChecks).FullName!.Replace('+', '.');
        var expectedReaders = new (string Method, string Use)[]
        {
            ($"{owner}.LoopbackHandler.HandleRequirementAsync", "ConnectionInfo.get_RemoteIpAddress"),
            ($"{owner}.Helper.IsInternal", "\"X-Forwarded-For\""),
            ($"{owner}.FeatureHandler.HandleRequirementAsync", "IHttpConnectionFeature.get_RemoteIpAddress"),
            ($"{owner}.LocalTrust.Trusted", "ConnectionInfo.get_LocalIpAddress"),
            ($"{owner}.AsyncHandler.HandleRequirementAsync", "ConnectionInfo.get_RemotePort"),
            ($"{owner}.HeaderFieldHandler.HandleRequirementAsync", "ForwardedHeadersDefaults.get_XForwardedForHeaderName"),
            ($"{owner}.ReviewedReader.Address", "ConnectionInfo.get_RemoteIpAddress"),
        };
        foreach (var (method, use) in expectedReaders)
        {
            Assert.True(found.Readers.TryGetValue(method, out var uses) && uses.Contains(use),
                $"{method}: {use} not found; readers: {string.Join("; ", found.Readers.Select(r => $"{r.Key}: {string.Join(", ", r.Value)}"))}");
        }
        Assert.False(found.Readers.ContainsKey($"{owner}.Clean.HandleRequirementAsync"), "flagged a handler that reads only the user");
        // Reviewing every reader leaves exactly the decisions that reach one.
        var reviewed = found.Readers.Keys.Select(k => (k, "planted")).ToList();
        var problems = ClientAddressCode.Check(found, reviewed);
        foreach (var handler in new[] { "LoopbackHandler", "HelperHandler", "FeatureHandler", "InterfaceHandler", "AsyncHandler", "HeaderFieldHandler" })
        {
            Assert.Contains(problems, p => p.StartsWith($"{owner}.{handler}.", StringComparison.Ordinal) && p.Contains("decides on where a request comes from", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(problems, p => p.Contains(".Clean.", StringComparison.Ordinal) || p.StartsWith($"{owner}.ReviewedReader", StringComparison.Ordinal));
        Assert.Equal(6, problems.Count);
        // Unreviewed readers fail too, and a stale entry.
        var unreviewed = ClientAddressCode.Check(found, [("Erp.Nowhere.Gone", "stale")]);
        Assert.Contains($"{ReviewedFile}: 'Erp.Nowhere.Gone' no longer reads where a request comes from; remove the entry", unreviewed);
        Assert.Contains(unreviewed, p => p.StartsWith($"{owner}.ReviewedReader.Address reads where a request comes from", StringComparison.Ordinal));
        Assert.True(expectedReaders.Length - 1 >= Ratchet.Min("g2.clientAddressCodePlantsCaught"),
            $"{expectedReaders.Length - 1} planted decisions caught; ratchet minimum {Ratchet.Min("g2.clientAddressCodePlantsCaught")}");
    }
}

/// <summary>Planted shapes for the client-address code scan's self-test (never registered).</summary>
internal static class PlantedAddressChecks
{
    internal sealed class LoopbackHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.Resource is HttpContext http && http.Connection.RemoteIpAddress is { } a && System.Net.IPAddress.IsLoopback(a))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    internal static class Helper
    {
        public static bool IsInternal(HttpContext http) => http.Request.Headers["X-Forwarded-For"].Count == 0;
    }

    internal sealed class HelperHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.Resource is HttpContext http && Helper.IsInternal(http))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class FeatureHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.Resource is HttpContext http && http.Features.Get<IHttpConnectionFeature>()?.RemoteIpAddress is null)
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    internal interface ITrust
    {
        bool Trusted(HttpContext http);
    }

    internal sealed class LocalTrust : ITrust
    {
        public bool Trusted(HttpContext http) => http.Connection.LocalIpAddress is not null;
    }

    internal sealed class InterfaceHandler(ITrust trust) : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.Resource is HttpContext http && trust.Trusted(http))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class AsyncHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            await Task.Yield();
            if (context.Resource is HttpContext http && http.Connection.RemotePort == 0)
            {
                context.Succeed(requirement);
            }
        }
    }

    internal sealed class HeaderFieldHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.Resource is HttpContext http && http.Request.Headers[Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersDefaults.XForwardedForHeaderName].Count > 0)
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class Clean : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.HasPermission(requirement.Permission))
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }

    internal static class ReviewedReader
    {
        public static string? Address(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>Finds the methods that read where a request comes from, and the methods the
/// authentication and authorization code reaches.</summary>
public static class ClientAddressCode
{
    /// <param name="Readers">Method (owner) to what it reads.</param>
    /// <param name="Roots">Methods of the authentication and authorization types.</param>
    /// <param name="Reached">Every method reachable from <paramref name="Roots"/>, with the root it was reached from first.</param>
    public sealed record Result(IReadOnlyDictionary<string, IReadOnlyList<string>> Readers, IReadOnlySet<string> Roots,
        IReadOnlyDictionary<string, string> Reached, int MethodsScanned);

    /// <summary>Headers a proxy or CDN names the client's address in (compared ignoring case).</summary>
    private static readonly HashSet<string> OriginHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "X-Forwarded-For", "X-Forwarded", "Forwarded", "Forwarded-For", "X-Original-For", "X-Real-IP", "X-Client-IP", "CF-Connecting-IP",
        "True-Client-IP", "X-Cluster-Client-IP", "Fastly-Client-IP", "X-Azure-ClientIP", "X-Envoy-External-Address", "X-ProxyUser-Ip",
    };

    private static readonly string[] OriginWords = ["Forwarded", "RealIp", "ClientIp"];

    /// <summary>Framework types whose instances decide or establish who a request is.</summary>
    private static readonly string[] AuthorizationTypes =
    [
        "Microsoft.AspNetCore.Authorization.IAuthorizationHandler", "Microsoft.AspNetCore.Authorization.IAuthorizationRequirement",
        "Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider", "Microsoft.AspNetCore.Authorization.IAuthorizationService",
        "Microsoft.AspNetCore.Authorization.IAuthorizationEvaluator", "Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler",
        "Microsoft.AspNetCore.Authorization.IAuthorizationHandlerProvider", "Microsoft.AspNetCore.Authorization.Policy.IPolicyEvaluator",
        "Microsoft.AspNetCore.Authentication.IAuthenticationHandler", "Microsoft.AspNetCore.Authentication.IAuthenticationService",
        "Microsoft.AspNetCore.Authentication.IClaimsTransformation",
    ];

    /// <summary>What a called framework method reads of the request's origin (null: nothing).</summary>
    public static string? Classify(MethodBase called)
    {
        var type = called.DeclaringType;
        if (type is null || IsProduct(type))
        {
            return null;
        }
        var full = (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName ?? "";
        var use = $"{type.Name}.{called.Name}";
        if (full == "Microsoft.AspNetCore.Http.ConnectionInfo" &&
            called.Name is "get_RemoteIpAddress" or "get_RemotePort" or "get_LocalIpAddress" or "get_LocalPort" or "get_ClientCertificate" or "GetClientCertificateAsync")
        {
            return use;
        }
        if (full is "Microsoft.AspNetCore.Http.Features.IHttpConnectionFeature" or "Microsoft.AspNetCore.Http.Features.ITlsConnectionFeature" ||
            full.StartsWith("Microsoft.AspNetCore.HttpOverrides.", StringComparison.Ordinal))
        {
            return use;
        }
        if (called is MethodInfo { IsGenericMethod: true } generic &&
            generic.GetGenericArguments().FirstOrDefault(a => a.FullName is "Microsoft.AspNetCore.Http.Features.IHttpConnectionFeature" or "Microsoft.AspNetCore.Http.Features.ITlsConnectionFeature") is { } feature)
        {
            return $"{use}<{feature.Name}>";
        }
        if (OriginWords.Any(w => called.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || type.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            return use;
        }
        return null;
    }

    private static string? ClassifyField(FieldInfo field) =>
        field.DeclaringType is { } type && !IsProduct(type) && OriginWords.Any(w => field.Name.Contains(w, StringComparison.OrdinalIgnoreCase))
            ? $"{type.Name}.{field.Name}"
            : null;

    private static bool IsProduct(Type type) => type.Assembly.GetName().Name is { } name && (name.StartsWith("Erp.", StringComparison.Ordinal) && !name.EndsWith(".Tests", StringComparison.Ordinal));

    private static bool IsAuthorizationType(Type type)
    {
        for (var t = type; t is not null; t = t.DeclaringType)
        {
            try
            {
                if (t.GetInterfaces().Any(i => AuthorizationTypes.Contains(i.FullName)) || BaseTypes(t).Any(b => b.FullName is { } f && f.StartsWith("Microsoft.AspNetCore.Authentication.AuthenticationHandler`1", StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            catch (TypeLoadException)
            {
            }
        }
        return false;

        static IEnumerable<Type> BaseTypes(Type t)
        {
            for (var b = t.BaseType; b is not null; b = b.BaseType)
            {
                yield return b.IsGenericType ? b.GetGenericTypeDefinition() : b;
            }
        }
    }

    public static Result Scan(IEnumerable<Assembly> assemblies, Func<Type, bool>? types = null)
    {
        var readers = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var calls = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var all = new List<Type>();
        foreach (var assembly in assemblies)
        {
            try
            {
                all.AddRange(assembly.GetTypes().Where(t => types?.Invoke(t) ?? true));
            }
            catch (ReflectionTypeLoadException e)
            {
                all.AddRange(e.Types.OfType<Type>().Where(t => types?.Invoke(t) ?? true));
            }
        }
        var scanned = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var concrete = all.Where(t => t is { IsClass: true, IsAbstract: false } or { IsValueType: true }).ToList();
        foreach (var type in all)
        {
            var authorization = IsAuthorizationType(type);
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                byte[]? il;
                try
                {
                    il = method.GetMethodBody()?.GetILAsByteArray();
                }
                catch (Exception e) when (e is BadImageFormatException or InvalidOperationException)
                {
                    continue;
                }
                if (il is null)
                {
                    continue;
                }
                scanned++;
                var owner = TenantSettingCode.Owner(method);
                if (authorization)
                {
                    roots.Add(owner);
                }
                if (!calls.TryGetValue(owner, out var targets))
                {
                    calls[owner] = targets = new HashSet<string>(StringComparer.Ordinal);
                }
                void Read(string use)
                {
                    if (!readers.TryGetValue(owner, out var uses))
                    {
                        readers[owner] = uses = new SortedSet<string>(StringComparer.Ordinal);
                    }
                    uses.Add(use);
                }
                var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
                var methodArguments = method is MethodInfo { IsGenericMethod: true } g ? g.GetGenericArguments() : null;
                foreach (var (operand, token) in OutsideProcessCode.Operands(il))
                {
                    try
                    {
                        switch (operand)
                        {
                            case OperandType.InlineString when OriginHeaders.Contains(method.Module.ResolveString(token)):
                                Read($"\"{method.Module.ResolveString(token)}\"");
                                break;
                            case OperandType.InlineField when method.Module.ResolveField(token, typeArguments, methodArguments) is { } field && ClassifyField(field) is { } fieldUse:
                                Read(fieldUse);
                                break;
                            case OperandType.InlineMethod when method.Module.ResolveMethod(token, typeArguments, methodArguments) is { } called:
                                if (Classify(called) is { } use)
                                {
                                    Read(use);
                                }
                                else if (called.DeclaringType is { } target && all.Contains(target.IsGenericType ? target.GetGenericTypeDefinition() : target))
                                {
                                    targets.Add(TenantSettingCode.Owner(called));
                                    foreach (var implementation in Implementations(called, concrete))
                                    {
                                        targets.Add(TenantSettingCode.Owner(implementation));
                                    }
                                }
                                break;
                        }
                    }
                    catch (Exception e) when (e is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException or MissingMemberException)
                    {
                    }
                }
            }
        }
        // Everything the authentication and authorization code reaches, breadth first.
        var reached = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var root in roots.Order(StringComparer.Ordinal))
        {
            reached[root] = root;
            queue.Enqueue(root);
        }
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in calls.GetValueOrDefault(current) ?? [])
            {
                if (reached.TryAdd(next, reached[current]))
                {
                    queue.Enqueue(next);
                }
            }
        }
        return new Result(readers.ToDictionary(r => r.Key, r => (IReadOnlyList<string>)r.Value.ToList(), StringComparer.Ordinal), roots, reached, scanned);
    }

    /// <summary>The product methods an interface, abstract or virtual call may land on.</summary>
    private static IEnumerable<MethodBase> Implementations(MethodBase called, IReadOnlyList<Type> concrete)
    {
        if (called is not MethodInfo { IsVirtual: true } virtualMethod || called.DeclaringType is not { } declaring)
        {
            yield break;
        }
        var open = declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring;
        foreach (var type in concrete)
        {
            if (declaring.IsInterface)
            {
                foreach (var implemented in type.GetInterfaces().Where(i => (i.IsGenericType ? i.GetGenericTypeDefinition() : i) == open))
                {
                    InterfaceMapping map;
                    try
                    {
                        map = type.GetInterfaceMap(implemented);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                    for (var i = 0; i < map.InterfaceMethods.Length; i++)
                    {
                        if (map.InterfaceMethods[i].Name == virtualMethod.Name && map.TargetMethods[i].DeclaringType == type)
                        {
                            yield return map.TargetMethods[i];
                        }
                    }
                }
            }
            else if (IsDerived(type, open))
            {
                foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (candidate.Name == virtualMethod.Name && candidate.GetBaseDefinition() is { } definition &&
                        (definition.DeclaringType?.IsGenericType == true ? definition.DeclaringType.GetGenericTypeDefinition() : definition.DeclaringType) is { } owner &&
                        IsDerivedOrSame(open, owner))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        static bool IsDerived(Type type, Type open)
        {
            for (var b = type.BaseType; b is not null; b = b.BaseType)
            {
                if ((b.IsGenericType ? b.GetGenericTypeDefinition() : b) == open)
                {
                    return true;
                }
            }
            return false;
        }

        static bool IsDerivedOrSame(Type type, Type ancestor)
        {
            for (var b = type; b is not null; b = b.BaseType)
            {
                if ((b.IsGenericType ? b.GetGenericTypeDefinition() : b) == ancestor)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public static IReadOnlyList<string> Check(Result found, IReadOnlyList<(string Entry, string Reason)> reviewed)
    {
        var problems = new List<string>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"{G2ClientAddressCodeTests.ReviewedFile}: '{entry}' needs a reason after '#'");
            }
            allowed.Add(entry);
        }
        foreach (var (method, uses) in found.Readers.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (found.Reached.TryGetValue(method, out var root))
            {
                // Never reviewable: a permission decision that depends on where the request came from.
                problems.Add($"{root} decides on where a request comes from: it reaches {method}, which reads {string.Join(", ", uses)}. " +
                             "Permissions come from the signed-in user's roles only, whatever the client address.");
            }
            else if (!allowed.Contains(method))
            {
                problems.Add($"{method} reads where a request comes from ({string.Join(", ", uses)}): only reviewed methods may; review it in {G2ClientAddressCodeTests.ReviewedFile}");
            }
        }
        foreach (var stale in allowed.Where(a => !found.Readers.ContainsKey(a)).Order(StringComparer.Ordinal))
        {
            problems.Add($"{G2ClientAddressCodeTests.ReviewedFile}: '{stale}' no longer reads where a request comes from; remove the entry");
        }
        return problems;
    }
}
