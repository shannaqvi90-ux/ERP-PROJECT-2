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
        module.Permissions("leaky.data.read", "leaky.data.update", "leaky.data.delete");
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

            // Bug 27 (critic p03 round 1, plant T2): "what can this person do", cached per person id
            // in a dictionary the endpoint lambda captures. The key has no tenant, so whoever asks
            // for an id second gets the answer of whoever asked first. People are created through
            // POST /people, so tenant B's own activity opens the route with the person it created,
            // an id that is not among the few ids per table the attack samples; only replaying
            // tenant B's exact route values (and having B open every id A sends) finds it.
            var accessCache = new System.Collections.Concurrent.ConcurrentDictionary<Guid, PersonCard>();
            group.MapGet("/people/{id:guid}/access", async (Guid id, ErpDbSession session) =>
            {
                if (accessCache.TryGetValue(id, out var hit))
                {
                    return Results.Ok(hit);
                }
                if (await PersonAsync(session, id) is not { } card)
                {
                    return Results.NotFound();
                }
                accessCache[id] = card;
                return Results.Ok(card);
            }).WithName("leaky.personAccess").WithSummary("Planted bug: a person's access view cached per id in a captured dictionary.").RequirePermission("leaky.data.read");

            // Bug 28 (critic p03 round 1, plant T1): the same per-id cache in a static field.
            group.MapGet("/people/{id:guid}/card", async (Guid id, ErpDbSession session) =>
            {
                if (PersonCards.TryGetValue(id, out var hit))
                {
                    return Results.Ok(hit);
                }
                if (await PersonAsync(session, id) is not { } card)
                {
                    return Results.NotFound();
                }
                PersonCards[id] = card;
                return Results.Ok(card);
            }).WithName("leaky.personCard").WithSummary("Planted bug: a person's card cached per id in a static dictionary.").RequirePermission("leaky.data.read");

            // People are users this module creates itself (tenant-bound, so this write is correct).
            group.MapPost("/people", async (NewPerson request, ErpDbSession session) =>
            {
                var id = Guid.NewGuid();
                // The address sorts after every seeded one, so the attack's sample of tenant B's
                // addresses (the first few of each column) still holds the seeded accounts the
                // planted e-mail lookups need.
                var email = $"zz.person.{id:N}@people.example";
                var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim();
                // Refused, never cut short: a shortened copy of another tenant's value would read as a leak.
                if (email.Length > 254 || displayName.Length > 200)
                {
                    return Results.BadRequest();
                }
                await using var command = new NpgsqlCommand(
                    "INSERT INTO identity.users (id, tenant_id, email, email_normalized, display_name, language, is_active) " +
                    "VALUES (@id, erp.current_tenant_id(), @e, @e, @n, 'en', true) ON CONFLICT DO NOTHING", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("e", email);
                command.Parameters.AddWithValue("n", displayName);
                return await command.ExecuteNonQueryAsync() == 1 ? Results.Created($"/api/leaky/people/{id}", new { id }) : Results.Conflict();
            }).WithName("leaky.createPerson").WithSummary("Creates a person (a user of this workspace).").RequirePermission("leaky.data.update");

            // Bug 29 (critic p03 round 1, plant P2): roles are created only within the caller's own
            // permissions, but deleting one never checks what it grants, so a user who may delete
            // roles removes roles granting far more than they hold.
            group.MapPost("/roles", async (NewRole request, ErpDbSession session, ICurrentUser caller) =>
            {
                var permissions = (request.Permissions ?? []).Distinct().ToArray();
                if (!permissions.All(caller.Has))
                {
                    return Results.Problem(statusCode: 403);
                }
                var id = Guid.NewGuid();
                var nameEn = request.NameEn ?? $"Leaky {id:N}";
                var nameAr = request.NameAr ?? $"مسرب {id:N}";
                if (nameEn.Length > 100 || nameAr.Length > 100)
                {
                    return Results.BadRequest();
                }
                await using var command = new NpgsqlCommand(
                    "INSERT INTO identity.roles (id, tenant_id, name_en, name_ar, permissions, is_system) VALUES (@id, erp.current_tenant_id(), @en, @ar, @p, false) ON CONFLICT DO NOTHING",
                    session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("en", nameEn);
                command.Parameters.AddWithValue("ar", nameAr);
                command.Parameters.AddWithValue("p", permissions);
                return await command.ExecuteNonQueryAsync() == 1 ? Results.Created($"/api/leaky/roles/{id}", new { id }) : Results.Conflict();
            }).WithName("leaky.createRole").WithSummary("Creates a role granting only what the caller holds.").RequirePermission("leaky.data.update");

            group.MapGet("/roles/{id:guid}", async (Guid id, ErpDbSession session) =>
            {
                await using var command = new NpgsqlCommand("SELECT name_en, permissions FROM identity.roles WHERE id = @id", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                await using var reader = await command.ExecuteReaderAsync();
                return await reader.ReadAsync()
                    ? Results.Ok(new { id, nameEn = reader.GetString(0), permissions = reader.GetFieldValue<string[]>(1) })
                    : Results.NotFound();
            }).WithName("leaky.getRole").WithSummary("One role.").RequirePermission("leaky.data.read");

            group.MapDelete("/roles/{id:guid}", async (Guid id, ErpDbSession session) =>
            {
                await using var command = new NpgsqlCommand(
                    "DELETE FROM identity.user_roles WHERE role_id = @id; DELETE FROM identity.roles WHERE id = @id AND NOT is_system",
                    session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                return await command.ExecuteNonQueryAsync() > 0 ? Results.NoContent() : Results.NotFound();
            }).WithName("leaky.deleteRole").WithSummary("Planted bug: deletes any role, whatever it grants.").RequirePermission("leaky.data.delete");

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

    /// <summary>Planted process-wide state: person cards cached per id, without the tenant.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, PersonCard> PersonCards = new();

    public sealed record PersonCard(Guid Id, string DisplayName, string Email, IReadOnlyList<Guid> RoleIds);

    public sealed record NewPerson(string? DisplayName);

    public sealed record NewRole(string? NameEn, string? NameAr, IReadOnlyList<string>? Permissions);

    private static async Task<PersonCard?> PersonAsync(ErpDbSession session, Guid id)
    {
        await using var command = new NpgsqlCommand(
            "SELECT u.display_name, u.email, coalesce(array_agg(ur.role_id) FILTER (WHERE ur.role_id IS NOT NULL), '{}') " +
            "FROM identity.users u LEFT JOIN identity.user_roles ur ON ur.user_id = u.id WHERE u.id = @id GROUP BY u.id",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new PersonCard(id, reader.GetString(0), reader.GetString(1), reader.GetFieldValue<Guid[]>(2)) : null;
    }

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
