using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// The rules the HTTP attack's trace applies to every setting statement (critic p00 round 4: the
/// trace judged which class sent a statement, never which tenant it set). The parser must read every
/// shape of setting change, with the value from the statement's own parameters, and the rules must
/// refuse each way of running SQL under another tenant.
/// </summary>
public sealed class G1TenantValueRuleTests
{
    private const string A = "0190a000-0000-7000-8000-00000000000a";
    private const string B = "0190a000-0000-7000-8000-00000000000b";

    private static Dictionary<string, string?> Params(params (string Name, string? Value)[] values)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var i = 1;
        foreach (var (name, value) in values)
        {
            map[name] = value;
            map["$" + i++] = value;
        }
        return map;
    }

    [Fact]
    public void Every_shape_of_setting_change_is_read_with_its_value()
    {
        var kernel = SqlSettings.Parse(
            "SELECT set_config('app.tenant_id', @tenant, true), set_config('app.tenant_tx', extract(epoch from now())::text, true), set_config('app.actor_id', @actor, true)",
            Params(("tenant", A), ("actor", "")));
        Assert.Equal(3, kernel.Count);
        Assert.Equal(("app.tenant_id", A, false, (bool?)true), (kernel[0].Name, kernel[0].Value, kernel[0].Computed, kernel[0].Local));
        Assert.True(kernel[1].Computed);
        Assert.Equal("app.tenant_tx", kernel[1].Name);

        // Positional parameters, a cast, a literal and pg_catalog's qualified name.
        var positional = SqlSettings.Parse("SELECT pg_catalog.set_config('app.tenant_id', $1::text, true)", Params(("p", B)));
        Assert.Equal(B, positional.Single().Value);
        var literal = SqlSettings.Parse($"SELECT set_config('app.tenant_id', '{B}', true)", Params());
        Assert.Equal(B, literal.Single().Value);
        // The name itself from a parameter is read too.
        var named = SqlSettings.Parse("SELECT set_config(@name, @value, true)", Params(("name", "app.tenant_id"), ("value", B)));
        Assert.Equal(("app.tenant_id", B), (named.Single().Name, named.Single().Value));

        // SET and SET LOCAL, with = and TO.
        var set = SqlSettings.Parse($"SET app.tenant_id = '{B}'; SET LOCAL app.tenant_id TO '{A}'", Params());
        Assert.Equal(2, set.Count);
        Assert.Equal((B, (bool?)false), (set[0].Value, set[0].Local));
        Assert.Equal((A, (bool?)true), (set[1].Value, set[1].Local));

        // What the statement computes cannot be read.
        var computedName = SqlSettings.Parse("SELECT set_config('app.' || 'tenant_id', $1, true)", Params(("p", B)));
        Assert.Null(computedName.Single().Name);
        var computedValue = SqlSettings.Parse("SELECT set_config('app.tenant_id', (SELECT id::text FROM tenancy.tenants LIMIT 1), true)", Params());
        Assert.True(computedValue.Single().Computed);
        var sessionWide = SqlSettings.Parse("SELECT set_config('app.tenant_id', @t, false)", Params(("t", A)));
        Assert.Equal(false, sessionWide.Single().Local);
        // Text that changes nothing.
        Assert.Empty(SqlSettings.Parse("SELECT current_setting('app.tenant_id', true)", Params()));
    }

    private static TracedSettingChange Change(string sql, IReadOnlyDictionary<string, string?> parameters, string? requiredTenant, string request = "r1") =>
        new(SqlSettings.Parse(sql, parameters).Single(), "x.y", "/api/x", "GET", request, requiredTenant, false, "Erp.Kernel.Data.ErpDbSession", 42, null);

    private static TracedBind Bind(string tenant, string request = "r1") =>
        new(tenant, "user", "x.y", "/api/x", "GET", false, null, null) { Request = request };

    [Fact]
    public void A_request_may_run_its_SQL_only_under_the_tenant_it_may_bind()
    {
        const string bind = "SELECT set_config('app.tenant_id', @tenant, true)";
        // Signed in as A on a permissioned endpoint: A passes, B fails, no tenant passes (fails closed).
        Assert.Empty(TenantBindingRules.TenantValueViolations([Change(bind, Params(("tenant", A)), A)], []));
        Assert.Contains("SQL ran under tenant " + B, TenantBindingRules.TenantValueViolations([Change(bind, Params(("tenant", B)), A)], []).Single(), StringComparison.Ordinal);
        Assert.Empty(TenantBindingRules.TenantValueViolations([Change(bind, Params(("tenant", "")), A)], []));
        // A principal without a tenant may bind nothing.
        Assert.Single(TenantBindingRules.TenantValueViolations([Change(bind, Params(("tenant", A)), Guid.Empty.ToString())], []));

        // Anonymous (sign-in, the session lookup): only a tenant the kernel's session declared
        // binding in the same request.
        Assert.Empty(TenantBindingRules.TenantValueViolations([Change(bind, Params(("tenant", B)), null)], [Bind(B)]));
        Assert.Single(TenantBindingRules.TenantValueViolations([Change(bind, Params(("tenant", B)), null)], [Bind(A)]));
        Assert.Single(TenantBindingRules.TenantValueViolations([Change(bind, Params(("tenant", B)), null)], [Bind(B, "another request")]));

        // Whole-connection settings, computed names and computed tenants are refused.
        Assert.Contains("whole connection", TenantBindingRules.TenantValueViolations(
            [Change("SELECT set_config('app.tenant_id', @tenant, false)", Params(("tenant", A)), A)], []).Single(), StringComparison.Ordinal);
        Assert.Contains("name the statement computes", TenantBindingRules.TenantValueViolations(
            [Change("SELECT set_config('app.' || 'tenant_id', @tenant, true)", Params(("tenant", A)), A)], []).Single(), StringComparison.Ordinal);
        Assert.Contains("computed by the statement", TenantBindingRules.TenantValueViolations(
            [Change("SELECT set_config('app.tenant_id', (SELECT max(id)::text FROM t), true)", Params(), A)], []).Single(), StringComparison.Ordinal);
    }
}
