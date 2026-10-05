using System.Reflection;
using System.Reflection.Emit;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, compiled code: the methods that name the tenant setting (<c>app.tenant_id</c>) are exactly
/// the reviewed ones (tests/Gates/tenant-setting-methods.txt), method by method rather than file by
/// file. Critic p00 round 4, plants T1c and T1d: moving the kernel's one reviewed
/// <c>set_config</c> statement into a private helper called twice kept the per-file count of the
/// source gate, and the helper rebound the tenant from a header. Here the helper is a method that
/// loads the statement's text and is not reviewed, so the gate fails until someone reads it. The
/// scan reads the string constants every method body loads (async state machines, lambdas and
/// local functions are counted for the method they belong to), in every product assembly the host
/// loads. Text built at run time is judged at run time by the HTTP attack's trace (the value every
/// statement gives the setting).
/// </summary>
public sealed class G1TenantSettingCodeTests
{
    public const string ReviewedFile = "tests/Gates/tenant-setting-methods.txt";

    [Fact]
    public void Only_reviewed_methods_name_the_tenant_setting()
    {
        var found = TenantSettingCode.Scan(TenantSettingCode.ProductAssemblies());
        var reviewed = Repo.ReadReviewedList(ReviewedFile);
        var problems = TenantSettingCode.Check(found, reviewed);
        TestContext.Current.TestOutputHelper?.WriteLine($"{found.MethodsScanned} methods scanned; {found.Methods.Count} name the tenant setting");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(found.MethodsScanned >= Ratchet.Min("g1.tenantSettingMethodsScanned"),
            $"g1.tenantSettingMethodsScanned: {found.MethodsScanned}; ratchet minimum {Ratchet.Min("g1.tenantSettingMethodsScanned")}");
    }

    [Fact]
    public void The_scan_finds_a_helper_that_names_the_tenant_setting()
    {
        // This test assembly's planted helpers: an async helper (state machine), a lambda and a
        // plain method, none reviewed.
        var found = TenantSettingCode.Scan([typeof(PlantedTenantSettingCode).Assembly]);
        var names = found.Methods.Keys.ToList();
        Assert.Contains($"{typeof(PlantedTenantSettingCode).FullName}.ApplyAsync", names);
        Assert.Contains($"{typeof(PlantedTenantSettingCode).FullName}.Rebind", names);
        Assert.Contains($"{typeof(PlantedTenantSettingCode).FullName}.Plain", names);
        var problems = TenantSettingCode.Check(found, []);
        Assert.Contains(problems, p => p.Contains($"{typeof(PlantedTenantSettingCode).FullName}.ApplyAsync", StringComparison.Ordinal));
        // A reviewed method passes; a reviewed method that no longer names the setting is stale.
        var reviewed = found.Methods.Keys.Select(k => (k, "planted")).Append(("Erp.Nowhere.Gone", "stale")).ToList();
        var stale = TenantSettingCode.Check(found, reviewed);
        Assert.Equal(["tests/Gates/tenant-setting-methods.txt: 'Erp.Nowhere.Gone' no longer names the tenant setting; remove the entry"], stale);
    }
}

/// <summary>Planted shapes for the scan's self-test (never called).</summary>
internal static class PlantedTenantSettingCode
{
    public static async Task ApplyAsync(Npgsql.NpgsqlConnection connection, string tenant)
    {
        await Task.Yield();
        await using var command = new Npgsql.NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", connection);
        command.Parameters.AddWithValue("t", tenant);
        await command.ExecuteNonQueryAsync();
    }

    public static Func<string> Rebind() => () => "SET LOCAL app.tenant_id = 'x'";

    public static string Plain() => "app.tenant_id";
}

/// <summary>Finds the methods whose bodies load a string constant naming the tenant setting.</summary>
public static class TenantSettingCode
{
    public sealed record Result(IReadOnlyDictionary<string, IReadOnlyList<string>> Methods, int MethodsScanned);

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    public static IReadOnlyList<Assembly> ProductAssemblies()
    {
        var found = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>([typeof(Program).Assembly]);
        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            if (!found.TryAdd(assembly.GetName().Name!, assembly))
            {
                continue;
            }
            foreach (var reference in assembly.GetReferencedAssemblies().Where(r => r.Name?.StartsWith("Erp.", StringComparison.Ordinal) == true))
            {
                queue.Enqueue(Assembly.Load(reference));
            }
        }
        return found.Values.OrderBy(a => a.GetName().Name, StringComparer.Ordinal).ToList();
    }

    public static Result Scan(IEnumerable<Assembly> assemblies)
    {
        var methods = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var scanned = 0;
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.OfType<Type>().ToArray();
            }
            foreach (var type in types)
            {
                // The OpenAPI generator's cache of documentation comments: prose for the API
                // description, never sent to the database.
                if (type.Namespace == "Microsoft.AspNetCore.OpenApi.Generated" && type.Name.EndsWith("XmlCommentCache", StringComparison.Ordinal))
                {
                    continue;
                }
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
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
                    foreach (var text in Strings(method.Module, il).Where(NamesTenantSetting))
                    {
                        var owner = Owner(method);
                        if (!methods.TryGetValue(owner, out var list))
                        {
                            methods[owner] = list = [];
                        }
                        list.Add(text.Length <= 120 ? text : text[..120] + "…");
                    }
                }
            }
        }
        return new Result(methods.ToDictionary(m => m.Key, m => (IReadOnlyList<string>)m.Value, StringComparer.Ordinal), scanned);
    }

    public static bool NamesTenantSetting(string text) => text.Contains("app.tenant_id", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Check(Result found, IReadOnlyList<(string Entry, string Reason)> reviewed)
    {
        var problems = new List<string>();
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"{G1TenantSettingCodeTests.ReviewedFile}: '{entry}' needs a reason after '#'");
            }
            allowed.Add(entry);
        }
        foreach (var (method, texts) in found.Methods.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            if (!allowed.Contains(method))
            {
                problems.Add($"{method} names the tenant setting ({string.Join(" | ", texts.Distinct())}): only reviewed methods may; " +
                             $"review it in {G1TenantSettingCodeTests.ReviewedFile} or let the kernel's session bind the tenant");
            }
        }
        foreach (var stale in allowed.Where(a => !found.Methods.ContainsKey(a)).Order(StringComparer.Ordinal))
        {
            problems.Add($"{G1TenantSettingCodeTests.ReviewedFile}: '{stale}' no longer names the tenant setting; remove the entry");
        }
        return problems;
    }

    /// <summary>The method a body belongs to: an async or iterator state machine
    /// (<c>&lt;Name&gt;d__3.MoveNext</c>), a lambda or a local function (<c>&lt;Name&gt;b__0</c>,
    /// <c>&lt;Name&gt;g__Local|1_0</c>) counts for the method it was written in.</summary>
    private static string Owner(MethodBase method)
    {
        var type = method.DeclaringType!;
        var name = method.Name;
        if (name.StartsWith('<') && name.IndexOf('>', StringComparison.Ordinal) is > 1 and var end)
        {
            name = name[1..end];
        }
        while (type.DeclaringType is not null && type.Name.StartsWith('<'))
        {
            var close = type.Name.IndexOf('>', StringComparison.Ordinal);
            if (close > 1)
            {
                name = type.Name[1..close];
            }
            type = type.DeclaringType;
        }
        while (type.DeclaringType is not null && type.Name.StartsWith('<'))
        {
            type = type.DeclaringType;
        }
        return $"{(type.FullName ?? type.Name).Replace('+', '.')}.{name}";
    }

    /// <summary>Every string constant (<c>ldstr</c>) a method body loads.</summary>
    private static IEnumerable<string> Strings(Module module, byte[] il)
    {
        var i = 0;
        while (i < il.Length)
        {
            short value = il[i];
            if (value == 0xFE && i + 1 < il.Length)
            {
                value = unchecked((short)(0xFE00 | il[i + 1]));
                i += 2;
            }
            else
            {
                i += 1;
            }
            if (!OpCodesByValue.TryGetValue(value, out var op))
            {
                yield break; // not IL we understand: stop rather than misread operands
            }
            if (op == OpCodes.Ldstr)
            {
                var token = BitConverter.ToInt32(il, i);
                string? text = null;
                try
                {
                    text = module.ResolveString(token);
                }
                catch (ArgumentException)
                {
                }
                if (text is not null)
                {
                    yield return text;
                }
            }
            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
    }
}
