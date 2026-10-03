using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        module.Services.AddSingleton<LastListHolder>();
        module.Endpoints(group =>
        {
            // Bug 9: a process-wide static cache of the workspace record, filled by whichever
            // tenant asks first (the shape of critic p01 round 1's plant A4).
            group.MapGet("/cached-tenant", async (ErpDbSession session) =>
            {
                if (cachedTenant is null)
                {
                    await using var command = new NpgsqlCommand("SELECT id::text || ' ' || name_en FROM tenancy.tenants", session.Connection, session.Transaction);
                    cachedTenant = (string?)await command.ExecuteScalarAsync();
                }
                return Results.Ok(new { tenant = cachedTenant });
            }).WithName("leaky.cachedTenant").WithSummary("Planted bug: caches the workspace in a static field.").RequirePermission("leaky.data.read");

            // Bug 10: a singleton that hands each caller the list the previous caller read.
            group.MapGet("/recent", async (ErpDbSession session, LastListHolder holder) =>
            {
                var current = await NamesAsync(session);
                var previous = Interlocked.Exchange(ref holder.Last, current);
                return Results.Ok(previous ?? current);
            }).WithName("leaky.recent").WithSummary("Planted bug: a singleton shares the last list read across tenants.").RequirePermission("leaky.data.read");

            // Bugs 1, 2, 3 and 6 build their own unit of work (outside dependency injection, so the
            // kernel's guard on the request's session cannot see them) and bind it to a tenant the
            // client chose. Bug 26 tries the same on the request's own session, which the kernel
            // refuses.

            // Bug 1: the tenant comes from a header the client controls.
            group.MapGet("/by-header", async (HttpContext http, NpgsqlDataSource dataSource, ErpDbSession session) =>
            {
                var tenant = http.Request.Headers["X-Tenant-Id"].ToString();
                if (Guid.TryParse(tenant, out var id))
                {
                    await using var rogue = await RogueAsync(dataSource, id);
                    return Results.Ok(await NamesAsync(rogue));
                }
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.byHeader").WithSummary("Planted bug: trusts X-Tenant-Id.").RequirePermission("leaky.data.read");

            // Bug 2: the tenant comes from the route.
            group.MapGet("/tenants/{id:guid}", async (Guid id, NpgsqlDataSource dataSource) =>
            {
                await using var rogue = await RogueAsync(dataSource, id);
                return Results.Ok(await NamesAsync(rogue));
            }).WithName("leaky.byRoute").WithSummary("Planted bug: trusts the route id.").RequirePermission("leaky.data.read");

            // Bug 3: a write that trusts a tenant id in the body.
            group.MapPut("/tenant", async (RenameRequest request, NpgsqlDataSource dataSource) =>
            {
                if (request.TenantId is { } id)
                {
                    await using var rogue = await RogueAsync(dataSource, id);
                    await using var command = new NpgsqlCommand("UPDATE tenancy.tenants SET name_en = name_en || ' (renamed)'", rogue.Connection, rogue.Transaction);
                    await command.ExecuteNonQueryAsync();
                    await rogue.CommitAsync();
                }
                return Results.NoContent();
            }).WithName("leaky.byBody").WithSummary("Planted bug: trusts tenantId in the body.").RequirePermission("leaky.data.update");

            // Bug 26: the route's tenant bound on the request's own session. The kernel refuses
            // (CrossTenantBindException, answered 404); the trace still reports the attempt.
            group.MapGet("/guarded/{id:guid}", async (Guid id, ErpDbSession session) =>
            {
                await session.RollbackAsync();
                await session.BeginAsync(id, null, "user");
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.guarded").WithSummary("Planted bug the kernel refuses: rebinds the request's session to the route's tenant.").RequirePermission("leaky.data.read");

            // Bug 21 (critic p00 round 2, plant A-hdr): a header with a name nobody would guess
            // switches the tenant with set_config on the request's own connection.
            group.MapGet("/acting", async (HttpContext http, ErpDbSession session) =>
            {
                if (Guid.TryParse(http.Request.Headers["X-Acting-For"].ToString(), out var id))
                {
                    await using var command = new NpgsqlCommand(
                        "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true)",
                        session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("t", id.ToString());
                    await command.ExecuteNonQueryAsync();
                }
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.acting").WithSummary("Planted bug: switches tenant from the X-Acting-For header with set_config.").RequirePermission("leaky.data.read");

            // Bug 23 (critic p01 round 2, plant B): a variable captured by the endpoint lambda
            // keeps the previous caller's workspace and hands it to the next caller in a header.
            string? previousCaller = null;
            group.MapGet("/previous", async (HttpContext http, ErpDbSession session) =>
            {
                await using var command = new NpgsqlCommand("SELECT id::text || ' ' || code || ' ' || name_en FROM tenancy.tenants", session.Connection, session.Transaction);
                var mine = (string?)await command.ExecuteScalarAsync();
                http.Response.Headers["X-Previous-Workspace"] = previousCaller ?? "";
                previousCaller = mine;
                return Results.Ok(new { ok = true });
            }).WithName("leaky.previous").WithSummary("Planted bug: a captured variable returns the previous caller's workspace in a header.").RequirePermission("leaky.data.read");

            // Bug 24 (critic p00 round 2, plant P2): a write guarded by a read permission.
            group.MapPost("/users/{id:guid}/reactivate", async (Guid id, ErpDbSession session) =>
            {
                await using var command = new NpgsqlCommand("UPDATE identity.users SET is_active = true WHERE id = @id", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                await command.ExecuteNonQueryAsync();
                return Results.NoContent();
            }).WithName("leaky.reactivate").WithSummary("Planted bug: reactivates a user with only a read permission.").RequirePermission("leaky.data.read");

            // Bug 25: a GET that writes. Every GET runs in a read-only transaction, so the database
            // refuses the write.
            group.MapGet("/touch", async (ErpDbSession session) =>
            {
                await using var command = new NpgsqlCommand("UPDATE tenancy.tenants SET name_en = name_en || ' (touched)'", session.Connection, session.Transaction);
                await command.ExecuteNonQueryAsync();
                return Results.Ok(new { touched = true });
            }).WithName("leaky.touch").WithSummary("Planted bug: a read that writes.").RequirePermission("leaky.data.read");

            // Bug 4: a lookup by e-mail through the reviewed sign-in function (the tenant is ignored).
            group.MapGet("/lookup", async (string? email, ErpDbSession session) =>
                Results.Ok(await ResolveLoginAsync(session, email ?? "")))
                .WithName("leaky.lookup").WithSummary("Planted bug: finds accounts by e-mail in every tenant.").RequirePermission("leaky.data.read");

            // Bug 5: an existence oracle. Answers only whether an e-mail exists somewhere.
            group.MapGet("/exists", async (string? email, ErpDbSession session) =>
                Results.Ok(new { exists = (await ResolveLoginAsync(session, email ?? "")).Count > 0 }))
                .WithName("leaky.exists").WithSummary("Planted bug: tells whether an e-mail exists in any tenant.").RequirePermission("leaky.data.read");

            // Bug 6: the tenant comes from a query parameter with an unguessable name.
            group.MapGet("/report", async (Guid? ownerReference, NpgsqlDataSource dataSource, ErpDbSession session) =>
            {
                if (ownerReference is { } id)
                {
                    await using var rogue = await RogueAsync(dataSource, id);
                    return Results.Ok(await NamesAsync(rogue));
                }
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.report").WithSummary("Planted bug: trusts the ownerReference query parameter.").RequirePermission("leaky.data.read");

            // Bug 7: a body text field used as a cross-tenant lookup key.
            group.MapPost("/find", async (FindRequest request, ErpDbSession session) =>
                Results.Ok(await ResolveLoginAsync(session, request.Reference ?? "")))
                .WithName("leaky.find").WithSummary("Planted bug: looks up the body's reference in every tenant.").RequirePermission("leaky.data.update");

            // Bug 8: grants whatever roles the body names to the caller (no check against the caller's own permissions).
            group.MapPost("/grants", async (GrantRequest request, ErpDbSession session, ICurrentUser caller) =>
            {
                foreach (var role in request.RoleIds ?? [])
                {
                    await using var command = new NpgsqlCommand(
                        "INSERT INTO identity.user_roles (id, tenant_id, user_id, role_id) VALUES (gen_random_uuid(), erp.current_tenant_id(), @u, @r) ON CONFLICT DO NOTHING",
                        session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("u", caller.UserId);
                    command.Parameters.AddWithValue("r", role);
                    await command.ExecuteNonQueryAsync();
                }
                return Results.Created("/api/leaky/grants", new { granted = request.RoleIds?.Count ?? 0 });
            }).WithName("leaky.grants").WithSummary("Planted bug: assigns any role to the caller.").RequirePermission("leaky.data.update");
        });
    }

    private static string? cachedTenant;

    /// <summary>Planted process-wide state: a singleton with a mutable field.</summary>
    public sealed class LastListHolder
    {
        public List<string>? Last;
    }

    public sealed record FindRequest(string? Reference);

    public sealed record GrantRequest(IReadOnlyList<Guid>? RoleIds);

    /// <summary>The planted lookups leave the request's tenant and call the reviewed sign-in
    /// lookup on the unbound connection, as a careless endpoint would: they prove the well-known
    /// gate password for every account with the address and return the workspaces that matched.</summary>
    private static async Task<List<object>> ResolveLoginAsync(ErpDbSession session, string email)
    {
        await session.RollbackAsync();
        var connection = await session.OpenUnboundAsync();
        var proofs = new List<string>();
        await using (var challenge = new NpgsqlCommand("SELECT challenge FROM identity.verify_sign_in(@e, NULL, 'leaky', NULL, NULL, 50, 1)", connection))
        {
            challenge.Parameters.AddWithValue("e", email);
            await using var reader = await challenge.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (PasswordHasher.Prove(Erp.Testing.ErpTestEnvironment.Password, reader.GetString(0)) is { } proof)
                {
                    proofs.Add(proof);
                }
            }
        }
        await using var command = new NpgsqlCommand("SELECT tenant_id FROM identity.verify_sign_in(@e, @p, 'leaky', NULL, NULL, 50, 1)", connection);
        command.Parameters.AddWithValue("e", email);
        command.Parameters.AddWithValue("p", proofs.ToArray());
        await using var rows = await command.ExecuteReaderAsync();
        var found = new List<object>();
        while (await rows.ReadAsync())
        {
            found.Add(new { tenantId = rows.GetGuid(0) });
        }
        return found;
    }

    public sealed record RenameRequest(Guid? TenantId, string? NameEn);

    /// <summary>A unit of work the planted code builds itself, bound to the tenant it was given.</summary>
    private static async Task<ErpDbSession> RogueAsync(NpgsqlDataSource dataSource, Guid tenant)
    {
        var rogue = new ErpDbSession(dataSource);
        await rogue.BeginAsync(tenant, null, "user");
        return rogue;
    }

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
