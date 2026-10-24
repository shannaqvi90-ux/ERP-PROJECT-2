using Erp.Kernel.Data;
using Erp.Modules.Numbering.Contracts;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Modules.Numbering.Tests;

/// <summary>A gate workspace pair (tenant A and tenant B, two companies with two branches each).</summary>
public sealed class NumberingFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>A unit of work as a later module's request or job would hold one, with the numbering
/// contract resolved from its scope.</summary>
internal sealed class Unit : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;

    private Unit(AsyncServiceScope scope)
    {
        _scope = scope;
        Session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        Numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumbers>();
    }

    public ErpDbSession Session { get; }

    public IDocumentNumbers Numbers { get; }

    /// <summary>System work (a job): every company of the tenant.</summary>
    public static async Task<Unit> JobAsync(ErpTestEnvironment env, Guid tenant)
    {
        var unit = new Unit(env.Factory.Services.CreateAsyncScope());
        await unit.Session.BeginAsync(tenant, null, "job");
        return unit;
    }

    /// <summary>A signed-in user's unit of work limited to <paramref name="companies"/>.</summary>
    public static async Task<Unit> UserAsync(ErpTestEnvironment env, Guid tenant, Guid user, params Guid[] companies)
    {
        var unit = new Unit(env.Factory.Services.CreateAsyncScope());
        await unit.Session.BeginAsync(tenant, user, ErpDbSession.UserActorKind);
        await unit.Session.BindCompaniesAsync(companies);
        unit.Session.SetBranchLimits([]);
        return unit;
    }

    public Task<IssuedNumber> TakeAsync(string type, Guid company, DateOnly date, Guid? branch = null) =>
        Numbers.TakeAsync(new NumberRequest(type, company, date, branch, Guid.NewGuid()), CancellationToken.None);

    public Task CommitAsync() => Session.CommitAsync();

    public Task RollbackAsync() => Session.RollbackAsync();

    public async ValueTask DisposeAsync() => await _scope.DisposeAsync();
}

internal static class Db
{
    public static async Task<List<Guid>> CompaniesAsync(ErpTestEnvironment env, Guid tenant)
    {
        await using var db = await env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM tenancy.companies WHERE tenant_id = @t ORDER BY code", db);
        command.Parameters.AddWithValue("t", tenant);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids;
    }

    public static async Task<List<Guid>> BranchesAsync(ErpTestEnvironment env, Guid company)
    {
        await using var db = await env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM tenancy.branches WHERE company_id = @c ORDER BY code", db);
        command.Parameters.AddWithValue("c", company);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }
        return ids;
    }

    public static async Task<Guid> UserAsync(ErpTestEnvironment env, string email)
    {
        await using var db = await env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM identity.users WHERE email = @e", db);
        command.Parameters.AddWithValue("e", email);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Changes a sequence directly (as a migration or an operator would), for a test's own set-up.</summary>
    public static async Task SetAsync(ErpTestEnvironment env, Guid company, string type, string prefix, string reset, bool gapless, bool perBranch = false, int padding = 5)
    {
        await using var db = await env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE numbering.sequences SET prefix = @p, reset = @r, gapless = @g, per_branch = @b, padding = @d WHERE company_id = @c AND document_type = @t", db);
        command.Parameters.AddWithValue("p", prefix);
        command.Parameters.AddWithValue("r", reset);
        command.Parameters.AddWithValue("g", gapless);
        command.Parameters.AddWithValue("b", perBranch);
        command.Parameters.AddWithValue("d", padding);
        command.Parameters.AddWithValue("c", company);
        command.Parameters.AddWithValue("t", type);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    public static async Task<long> ScalarAsync(ErpTestEnvironment env, string sql, params (string Name, object Value)[] parameters)
    {
        await using var db = await env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<string> TextAsync(ErpTestEnvironment env, string sql, params (string Name, object Value)[] parameters)
    {
        await using var db = await env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return (await command.ExecuteScalarAsync())?.ToString() ?? "";
    }
}
