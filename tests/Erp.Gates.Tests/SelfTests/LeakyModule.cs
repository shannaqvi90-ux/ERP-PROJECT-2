using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// A deliberately broken module, hosted only by the gate self-tests, with the classic tenant
/// isolation bugs: it trusts a tenant header, a route id and a body field. The isolation gate
/// must catch every one of them; if it does not, the gate is blind and the self-test fails.
/// </summary>
public sealed class LeakyModule : ErpModule
{
    public override string Name => "leaky";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions("leaky.data.read", "leaky.data.update");
        module.Endpoints(group =>
        {
            // Bug 1: the tenant comes from a header the client controls.
            group.MapGet("/by-header", async (HttpContext http, ErpDbSession session) =>
            {
                var tenant = http.Request.Headers["X-Tenant-Id"].ToString();
                if (Guid.TryParse(tenant, out var id))
                {
                    await session.RollbackAsync();
                    await session.BeginAsync(id, null, "user");
                }
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.byHeader").WithSummary("Planted bug: trusts X-Tenant-Id.").RequirePermission("leaky.data.read");

            // Bug 2: the tenant comes from the route.
            group.MapGet("/tenants/{id:guid}", async (Guid id, ErpDbSession session) =>
            {
                await session.RollbackAsync();
                await session.BeginAsync(id, null, "user");
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.byRoute").WithSummary("Planted bug: trusts the route id.").RequirePermission("leaky.data.read");

            // Bug 3: a write that trusts a tenant id in the body.
            group.MapPut("/tenant", async (RenameRequest request, ErpDbSession session) =>
            {
                if (request.TenantId is { } id)
                {
                    await session.RollbackAsync();
                    await session.BeginAsync(id, null, "user");
                    await using var command = new NpgsqlCommand("UPDATE tenancy.tenants SET name_en = name_en || ' (renamed)'", session.Connection, session.Transaction);
                    await command.ExecuteNonQueryAsync();
                }
                return Results.NoContent();
            }).WithName("leaky.byBody").WithSummary("Planted bug: trusts tenantId in the body.").RequirePermission("leaky.data.update");
        });
    }

    public sealed record RenameRequest(Guid? TenantId, string? NameEn);

    private static async Task<List<string>> NamesAsync(ErpDbSession session)
    {
        await using var command = new NpgsqlCommand("SELECT display_name || ' ' || email FROM identity.users", session.Connection, session.Transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }
}
