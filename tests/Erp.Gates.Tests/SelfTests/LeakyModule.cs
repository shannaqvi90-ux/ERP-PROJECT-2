using Erp.Kernel.Data;
using Erp.Kernel.Http;
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
        module.Services.AddSingleton<CountCache>();
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

            // Bugs 31 and 32 (p06 gates first): the same static cache behind exports. Tenant B's
            // name reaches tenant A inside a compressed PDF content stream and inside a spreadsheet
            // cell of a zipped workbook, where a search of the raw bytes sees nothing; the gate
            // reads them as a reader would.
            group.MapGet("/export.pdf", async (ErpDbSession session) =>
            {
                exportedTenant ??= await FirstTenantNameAsync(session);
                return Results.File(PlantedExports.Pdf(exportedTenant ?? ""), "application/pdf", "leaky.pdf");
            }).WithName("leaky.exportPdf").WithSummary("Planted bug: a PDF export of a workspace name cached in a static field.")
              .Surface(SurfaceKind.Export).RequirePermission("leaky.data.read");
            group.MapGet("/export.xlsx", async (ErpDbSession session) =>
            {
                exportedTenant ??= await FirstTenantNameAsync(session);
                return Results.File(PlantedExports.Xlsx(exportedTenant ?? ""), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "leaky.xlsx");
            }).WithName("leaky.exportXlsx").WithSummary("Planted bug: a workbook export of a workspace name cached in a static field.")
              .Surface(SurfaceKind.Export).RequirePermission("leaky.data.read");

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

            // Bug 40 (critic p00 round 4, plant T1d): the tenant from a header found by looping over
            // the request's headers, with a name nobody can guess (it changes every run), set with
            // set_config on the request's own connection. The attack never learns the name, so it
            // never sends it: the recorder must report the enumeration itself.
            group.MapGet("/support", async (HttpContext http, ErpDbSession session) =>
            {
                foreach (var header in http.Request.Headers)
                {
                    if (header.Key.Equals(SupportHeader, StringComparison.OrdinalIgnoreCase) && Guid.TryParse(header.Value.ToString(), out var id))
                    {
                        await using var command = new NpgsqlCommand(
                            "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true)",
                            session.Connection, session.Transaction);
                        command.Parameters.AddWithValue("t", id.ToString());
                        await command.ExecuteNonQueryAsync();
                    }
                }
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.support").WithSummary("Planted bug: switches tenant from a header found by enumerating the headers.").RequirePermission("leaky.data.read");

            // A query parameter picked out of the raw query string by a name the code compares
            // itself (p06): the recorder never learns the name, so the read of the raw string is
            // reported, while the framework's own parse of the query (every report reads its
            // parameters by name from the parsed query) is not.
            group.MapGet("/raw-query", async (HttpContext http, ErpDbSession session) =>
            {
                var raw = http.Request.QueryString.Value ?? "";
                var picked = raw.TrimStart('?').Split('&').FirstOrDefault(p => p.StartsWith("support-" + "ref=", StringComparison.Ordinal));
                return Results.Ok(new { picked = picked is not null, names = await NamesAsync(session) });
            }).WithName("leaky.rawQuery").WithSummary("Planted bug: picks a query parameter out of the raw query string.").RequirePermission("leaky.data.read");

            // Bug 42 (critic p00 round 4, plant T1c without a visible effect): the tenant from a
            // header read by name, set with set_config, then a query whose answer never reaches the
            // response. No tenant B data shows, so only the value the statement set gives it away.
            group.MapGet("/silent", async (HttpContext http, ErpDbSession session) =>
            {
                if (http.Request.Headers.TryGetValue("X-Support-Silent", out var header) && Guid.TryParse(header.ToString(), out var id))
                {
                    await using var command = new NpgsqlCommand(
                        "SELECT set_config('app.tenant_id', @t, true), set_config('app.tenant_tx', extract(epoch from now())::text, true)",
                        session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("t", id.ToString());
                    await command.ExecuteNonQueryAsync();
                    await using var count = new NpgsqlCommand("SELECT count(*) FROM identity.users", session.Connection, session.Transaction);
                    await count.ExecuteScalarAsync();
                }
                return Results.Ok(new { done = true });
            }).WithName("leaky.silent").WithSummary("Planted bug: switches tenant from a header and queries, showing nothing.").RequirePermission("leaky.data.read");

            // Bug 41: a pool of its own, built outside the platform, where the trace cannot read the
            // values its statements set (only the platform's pools are observed).
            group.MapGet("/own-pool", async (Microsoft.Extensions.Configuration.IConfiguration configuration, ErpDbSession session) =>
            {
                await using var pool = new NpgsqlDataSourceBuilder(Microsoft.Extensions.Configuration.ConfigurationExtensions.GetConnectionString(configuration, "App")).Build();
                await using var connection = await pool.OpenConnectionAsync();
                await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_catalog.pg_class", connection);
                await command.ExecuteScalarAsync();
                return Results.Ok(await NamesAsync(session));
            }).WithName("leaky.ownPool").WithSummary("Planted bug: queries through a pool of its own.").RequirePermission("leaky.data.read");

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

            // Bug 40 (lead, round 4; the shape of critic p05 round 1's plant L3): a count cache in a
            // singleton, keyed by the search text without the tenant. Tenant A is answered tenant
            // B's number of matching people: no id, no text, nothing of tenant B's to recognise.
            group.MapGet("/people-count", async (string? search, ErpDbSession session, CountCache cache) =>
            {
                var key = search ?? "";
                if (!cache.Totals.TryGetValue(key, out var total))
                {
                    await using var command = new NpgsqlCommand("SELECT count(*) FROM identity.users WHERE display_name ILIKE '%' || @s || '%'", session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("s", key);
                    total = (long)(await command.ExecuteScalarAsync())!;
                    cache.Totals[key] = total;
                }
                return Results.Ok(new { total });
            }).WithName("leaky.peopleCount").WithSummary("Planted bug: a singleton caches people counts by search text without the tenant.").RequirePermission("leaky.data.read");

            // Bug 41 (lead, round 4; the shape of critic p04 round 1's closure plants, but carrying
            // only a number): a variable captured by the endpoint lambda remembers the previous
            // caller's directory size (the letters of every address) and answers the change since
            // then to the next caller in the body.
            var previousHeadCount = new long[1];
            group.MapGet("/head-count", async (ErpDbSession session) =>
            {
                await using var command = new NpgsqlCommand("SELECT coalesce(sum(length(email)), 0) FROM identity.users", session.Connection, session.Transaction);
                var mine = (long)(await command.ExecuteScalarAsync())!;
                var previous = Interlocked.Exchange(ref previousHeadCount[0], mine);
                return Results.Ok(new { letters = mine, change = mine - previous });
            }).WithName("leaky.headCount").WithSummary("Planted bug: a captured variable answers the change since the previous caller's directory size.").RequirePermission("leaky.data.read");

            // Bugs 27 and 28 (critic p04 round 1, plants P1b and P1c): a write endpoint whose lambda
            // captures an array and keeps the previous writer's e-mail in it. Only a valid body
            // reaches the code (as with the real preferences endpoint), so the leak shows only when
            // a valid write of one tenant follows a valid write of the other. Bug 27 hands the
            // previous writer to the next one in a response header, with a correct body; bug 28
            // appends it to the display name in the body.
            var previousEditor = new string?[1];
            group.MapPut("/me/theme", async (ThemeRequest request, ErpDbSession session, ICurrentUser caller, HttpContext http) =>
            {
                if (request.Theme is not ("calm" or "bright"))
                {
                    return Results.BadRequest();
                }
                var mine = await EmailAsync(session, caller.UserId);
                var last = previousEditor[0];
                previousEditor[0] = mine;
                if (last is not null)
                {
                    http.Response.Headers["X-Erp-Previous-Editor"] = last;
                }
                return Results.Ok(new { email = mine, theme = request.Theme });
            }).WithName("leaky.theme").WithSummary("Planted bug: a captured array hands the previous writer's e-mail to the next writer in a header.").RequirePermission("leaky.data.update");

            // Bug 28 has the exact shape of the critic's plant: a synchronous lambda passes the
            // captured array of a product record to a static handler.
            var previousSaver = new LeakySaver?[1];
            group.MapPut("/me/density", (DensityRequest request, ErpDbSession session, ICurrentUser caller) => SaveDensityAsync(request, session, caller, previousSaver))
                .WithName("leaky.density").WithSummary("Planted bug: a captured array appends the previous writer's e-mail to the display name.").RequirePermission("leaky.data.update");

            // Bug 43 (critic p04 round 3, plant N1): a "me" endpoint with an optional, documented
            // userId acts on whichever user the body names. The caller holds the endpoint's own
            // permission, so only the object-level check (G2 SubjectInjection) can see it.
            group.MapPut("/me/language", async (LanguageForRequest request, ErpDbSession session, ICurrentUser caller) =>
            {
                if (request.Language is not ("en" or "ar"))
                {
                    return Results.BadRequest();
                }
                await using var command = new NpgsqlCommand("UPDATE identity.users SET language = @l WHERE id = @u", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("l", request.Language);
                command.Parameters.AddWithValue("u", request.UserId ?? caller.UserId);
                await command.ExecuteNonQueryAsync();
                return Results.Ok(new { language = request.Language });
            }).WithName("leaky.languageFor").WithSummary("Planted bug: changes the language of any user the body names.").RequirePermission("leaky.data.update");

            // Bug 44: the same, with the user named in a header nobody documents.
            group.MapPut("/me/nickname", async (NicknameRequest request, ErpDbSession session, ICurrentUser caller, HttpContext http) =>
            {
                if (string.IsNullOrWhiteSpace(request.Nickname))
                {
                    return Results.BadRequest();
                }
                var target = Guid.TryParse(http.Request.Headers["X-On-Behalf-Of"].ToString(), out var named) ? named : caller.UserId;
                await using var command = new NpgsqlCommand("UPDATE identity.users SET display_name = @n WHERE id = @u", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("n", request.Nickname);
                command.Parameters.AddWithValue("u", target);
                await command.ExecuteNonQueryAsync();
                return Results.Ok(new { nickname = request.Nickname });
            }).WithName("leaky.nicknameFor").WithSummary("Planted bug: renames the user an X-On-Behalf-Of header names.").RequirePermission("leaky.data.update");

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
                    // Like the real endpoint, a system role (and so its members' access) is never touched:
                    // the attack aims this route at tenant A's own Administrator role too, and taking the
                    // administrator's access away would blind every later self-test.
                    "DELETE FROM identity.user_roles WHERE role_id = @id AND role_id IN (SELECT id FROM identity.roles WHERE NOT is_system); DELETE FROM identity.roles WHERE id = @id AND NOT is_system",
                    session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                return await command.ExecuteNonQueryAsync() > 0 ? Results.NoContent() : Results.NotFound();
            }).WithName("leaky.deleteRole").WithSummary("Planted bug: deletes any role, whatever it grants.").RequirePermission("leaky.data.delete");

            // Bug 30 (critic p03 round 2, plant P5): members are created and edited only within the
            // caller's grants, and an edit checks that the member holds nothing the caller lacks,
            // except when only the e-mail changes: that path skips the check, so a clerk moves the
            // Administrator's sign-in to an address of their choosing.
            group.MapPost("/members", async (MemberRequest request, ErpDbSession session, ICurrentUser caller) =>
            {
                var email = request.Email?.Trim() ?? "";
                var displayName = request.DisplayName?.Trim() ?? "";
                if (!email.Contains('@') || email.Length > 254 || displayName.Length is 0 or > 200)
                {
                    return Results.BadRequest();
                }
                var roleIds = (request.RoleIds ?? []).Distinct().ToList();
                if (!(await RolePermissionsAsync(session, roleIds)).All(caller.Has))
                {
                    return Results.Problem(statusCode: 403);
                }
                var id = Guid.NewGuid();
                await using (var command = new NpgsqlCommand(
                    "INSERT INTO identity.users (id, tenant_id, email, email_normalized, display_name, language, is_active) " +
                    "VALUES (@id, erp.current_tenant_id(), @e, lower(@e), @n, 'en', true) ON CONFLICT DO NOTHING", session.Connection, session.Transaction))
                {
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("e", email);
                    command.Parameters.AddWithValue("n", displayName);
                    if (await command.ExecuteNonQueryAsync() != 1)
                    {
                        return Results.Conflict();
                    }
                }
                await SetMemberRolesAsync(session, id, roleIds);
                return Results.Created($"/api/leaky/members/{id}", new { id });
            }).WithName("leaky.createMember").WithSummary("Creates a member with roles within the caller's own grants.").RequirePermission("leaky.data.update");

            group.MapGet("/members/{id:guid}", async (Guid id, ErpDbSession session) =>
                await MemberAsync(session, id) is { } member ? Results.Ok(member) : Results.NotFound())
                .WithName("leaky.getMember").WithSummary("One member.").RequirePermission("leaky.data.read");

            group.MapPut("/members/{id:guid}", async (Guid id, MemberRequest request, ErpDbSession session, ICurrentUser caller) =>
            {
                var email = request.Email?.Trim() ?? "";
                var displayName = request.DisplayName?.Trim() ?? "";
                if (!email.Contains('@') || email.Length > 254 || displayName.Length is 0 or > 200)
                {
                    return Results.BadRequest();
                }
                if (await MemberAsync(session, id) is not { } member)
                {
                    return Results.NotFound();
                }
                var roleIds = (request.RoleIds ?? []).Distinct().ToList();
                var rolesChanged = !member.RoleIds.ToHashSet().SetEquals(roleIds);
                if (rolesChanged && !(await RolePermissionsAsync(session, member.RoleIds.Except(roleIds).Concat(roleIds.Except(member.RoleIds)).ToList())).All(caller.Has))
                {
                    return Results.Problem(statusCode: 403);
                }
                var onlyEmail = email != member.Email && displayName == member.DisplayName && !rolesChanged;
                if (!onlyEmail && !(await RolePermissionsAsync(session, [.. member.RoleIds])).All(caller.Has))
                {
                    return Results.Problem(statusCode: 403);
                }
                await using (var command = new NpgsqlCommand(
                    "UPDATE identity.users SET email = @e, email_normalized = lower(@e), display_name = @n WHERE id = @id", session.Connection, session.Transaction))
                {
                    command.Parameters.AddWithValue("id", id);
                    command.Parameters.AddWithValue("e", email);
                    command.Parameters.AddWithValue("n", displayName);
                    await command.ExecuteNonQueryAsync();
                }
                if (rolesChanged)
                {
                    await SetMemberRolesAsync(session, id, roleIds);
                }
                return Results.Ok(await MemberAsync(session, id));
            }).WithName("leaky.updateMember").WithSummary("Planted bug: an e-mail-only edit skips the check on the member's access.").RequirePermission("leaky.data.update");

            // Bug 31 (critic p03 round 2, plant L4): a registry of every address ever created, kept
            // in a file outside the tenant's rows (no static field, no index), so creating an
            // account answers 409 for an address another tenant created and 201 otherwise.
            group.MapPost("/accounts", async (AccountRequest request, ErpDbSession session) =>
            {
                var email = request.Email?.Trim().ToLowerInvariant() ?? "";
                if (!email.Contains('@') || email.Length > 254)
                {
                    return Results.BadRequest();
                }
                var registry = Path.Combine(Path.GetTempPath(), $"erp-leaky-accounts-{Environment.ProcessId}.txt");
                lock (typeof(AccountRequest))
                {
                    if (File.Exists(registry) && File.ReadLines(registry).Contains(email))
                    {
                        return Results.Conflict();
                    }
                    File.AppendAllLines(registry, [email]);
                }
                var id = Guid.NewGuid();
                await using var command = new NpgsqlCommand(
                    "INSERT INTO identity.users (id, tenant_id, email, email_normalized, display_name, language, is_active) " +
                    "VALUES (@id, erp.current_tenant_id(), @e, @e, @e, 'en', true) ON CONFLICT DO NOTHING", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("e", email);
                return await command.ExecuteNonQueryAsync() == 1 ? Results.Created($"/api/leaky/accounts/{id}", new { id }) : Results.Conflict();
            }).WithName("leaky.createAccount").WithSummary("Planted bug: refuses an address another tenant created (a registry on disk).").RequirePermission("leaky.data.update");

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

            // Bug 11: widens the request's company scope to every company of the tenant and
            // returns the companies' legal names (a user of company X reads company Y).
            group.MapGet("/company-names", async (ErpDbSession session) =>
            {
                await using (var widen = new NpgsqlCommand("SELECT set_config('app.company_scope', 'all', true)", session.Connection, session.Transaction))
                {
                    await widen.ExecuteNonQueryAsync();
                }
                await using var command = new NpgsqlCommand("SELECT id::text || ' ' || legal_name_en FROM tenancy.companies ORDER BY id", session.Connection, session.Transaction);
                await using var reader = await command.ExecuteReaderAsync();
                var names = new List<string>();
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }
                return Results.Ok(names);
            }).WithName("leaky.companyNames").WithSummary("Planted bug: reads every company of the tenant.").RequirePermission("leaky.data.read");

            // Bug 12 (critic p02 round 2, plant C2): adds the body's company to the request's scope
            // before writing, so a user of company X creates a branch in company Y. Its body must
            // pass validation (a well-formed e-mail and code) before anything is written.
            group.MapPost("/company-branches", async (LeakyBranchRequest request, ErpDbSession session) =>
            {
                if (request.CompanyId is not { } companyId || request.Code is not { Length: >= 2 } code || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$") ||
                    string.IsNullOrWhiteSpace(request.NameEn) || request.Email is not { } email || !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["invalid"] });
                }
                await session.IncludeNewCompanyAsync(companyId);
                var id = Guid.CreateVersion7();
                await using var command = new NpgsqlCommand(
                    "INSERT INTO tenancy.branches (id, tenant_id, company_id, code, name_en, name_ar, country, email, is_active, created_at, updated_at) " +
                    "VALUES (@id, erp.current_tenant_id(), @c, @code, @name, '', 'AE', @email, true, now(), now())", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("c", companyId);
                command.Parameters.AddWithValue("code", code);
                command.Parameters.AddWithValue("name", request.NameEn!.Trim());
                command.Parameters.AddWithValue("email", email);
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (PostgresException)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["companyId"] = ["refused"] });
                }
                return Results.Created($"/api/leaky/company-branches/{id}", new { id });
            }).WithName("leaky.companyBranches").WithSummary("Planted bug: creates a branch in any company of the tenant.").RequirePermission("leaky.data.update");

            // Bug 13 (critic p02 round 2): creates a company with the body's code and answers 409
            // when the code is taken, by a company the caller cannot see as well (a write oracle).
            group.MapPost("/companies", async (LeakyCompanyRequest request, ErpDbSession session, ICurrentUser caller) =>
            {
                if (request.Code is not { } code || !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9-]{1,19}$") || string.IsNullOrWhiteSpace(request.LegalNameEn))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = ["invalid"] });
                }
                var id = Guid.CreateVersion7();
                await session.IncludeNewCompanyAsync(id);
                await using (var savepoint = new NpgsqlCommand("SAVEPOINT leaky_company", session.Connection, session.Transaction))
                {
                    await savepoint.ExecuteNonQueryAsync();
                }
                await using var command = new NpgsqlCommand(
                    "INSERT INTO tenancy.companies (id, tenant_id, company_id, code, legal_name_en, legal_name_ar, base_currency, fiscal_year_start_month, fiscal_year_start_day, country, is_active, created_at, updated_at) " +
                    "VALUES (@id, erp.current_tenant_id(), @id, @code, @name, '', 'AED', 1, 1, 'AE', true, now(), now())", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("code", code);
                command.Parameters.AddWithValue("name", request.LegalNameEn!.Trim());
                try
                {
                    await command.ExecuteNonQueryAsync();
                }
                catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
                {
                    await using var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT leaky_company", session.Connection, session.Transaction);
                    await rollback.ExecuteNonQueryAsync();
                    return Results.Conflict(new { title = "taken" });
                }
                // As the product did then, the creator works in the new company; so do the users who
                // worked in every company before it, so the self-test environment keeps its
                // whole-workspace administrators (only they may create companies through the product).
                await using (var access = new NpgsqlCommand(
                    "INSERT INTO tenancy.user_company_access (id, tenant_id, user_id, company_id, all_branches, created_at, updated_at) " +
                    "SELECT gen_random_uuid(), erp.current_tenant_id(), u, @id, true, now(), now() FROM (" +
                    "  SELECT @caller AS u UNION SELECT t.user_id FROM tenancy.user_company_totals t " +
                    "   WHERE t.company_count = (SELECT company_count - 1 FROM tenancy.tenants WHERE id = erp.current_tenant_id())) w " +
                    "ON CONFLICT DO NOTHING", session.Connection, session.Transaction))
                {
                    access.Parameters.AddWithValue("id", id);
                    access.Parameters.AddWithValue("caller", caller.UserId);
                    await access.ExecuteNonQueryAsync();
                }
                return Results.Created($"/api/leaky/companies/{id}", new { id });
            }).WithName("leaky.companies").WithSummary("Planted bug: tells whether any company of the tenant has the code.").RequirePermission("leaky.data.update");

            // Bug 14 (critic p02 round 2, plant P2 and the access takeover): gives the user every
            // company and branch the body names that the caller can see, on anyone, the caller too,
            // with no check of what the caller or the user holds. (It never removes access, so the
            // self-test environment keeps its administrators.)
            group.MapPut("/company-access/{userId:guid}", async (Guid userId, LeakyAccessRequest request, ErpDbSession session) =>
            {
                foreach (var company in request.Companies ?? [])
                {
                    await using var command = new NpgsqlCommand(
                        "INSERT INTO tenancy.user_company_access (id, tenant_id, user_id, company_id, all_branches, created_at, updated_at) " +
                        "SELECT gen_random_uuid(), erp.current_tenant_id(), @u, c.id, @all, now(), now() FROM tenancy.companies c WHERE c.id = @c " +
                        "ON CONFLICT (tenant_id, user_id, company_id) DO UPDATE SET all_branches = EXCLUDED.all_branches OR tenancy.user_company_access.all_branches; " +
                        "INSERT INTO tenancy.user_branch_access (id, tenant_id, user_id, company_id, branch_id, created_at, updated_at) " +
                        "SELECT gen_random_uuid(), erp.current_tenant_id(), @u, b.company_id, b.id, now(), now() FROM tenancy.branches b " +
                        "WHERE b.company_id = @c AND b.id = ANY(@b) AND NOT @all ON CONFLICT DO NOTHING",
                        session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("u", userId);
                    command.Parameters.AddWithValue("c", company.CompanyId ?? Guid.Empty);
                    command.Parameters.AddWithValue("all", company.AllBranches ?? true);
                    command.Parameters.AddWithValue("b", (company.BranchIds ?? []).ToArray());
                    await command.ExecuteNonQueryAsync();
                }
                return Results.Ok(new { userId });
            }).WithName("leaky.companyAccess").WithSummary("Planted bug: gives company access with no grant check.").RequirePermission("leaky.data.update");

            // Bug 15 (critic p02 round 3, plant P3): the grant check without its permission rule.
            // Refuses the caller's own access and a user who works in a company the caller does
            // not, but never compares the user's permissions with the caller's: a clerk who works
            // in every company changes the Administrator's access. (Like bug 14 it only adds
            // access, so the self-test environment keeps its administrators.)
            group.MapPut("/company-access-partly-checked/{userId:guid}", async (Guid userId, LeakyAccessRequest request, ErpDbSession session, ICurrentUser caller) =>
            {
                if (userId == caller.UserId)
                {
                    return Results.Problem(statusCode: 403, title: "own access");
                }
                await using (var count = new NpgsqlCommand(
                    "SELECT coalesce((SELECT company_count FROM tenancy.user_company_totals WHERE user_id = @u), 0) > (SELECT count(*) FROM tenancy.user_company_access WHERE user_id = @u)",
                    session.Connection, session.Transaction))
                {
                    count.Parameters.AddWithValue("u", userId);
                    if ((bool)(await count.ExecuteScalarAsync())!)
                    {
                        return Results.Problem(statusCode: 403, title: "works in companies the caller does not");
                    }
                }
                foreach (var company in request.Companies ?? [])
                {
                    await using var command = new NpgsqlCommand(
                        "INSERT INTO tenancy.user_company_access (id, tenant_id, user_id, company_id, all_branches, created_at, updated_at) " +
                        "SELECT gen_random_uuid(), erp.current_tenant_id(), @u, c.id, @all, now(), now() FROM tenancy.companies c WHERE c.id = @c " +
                        "ON CONFLICT (tenant_id, user_id, company_id) DO UPDATE SET all_branches = EXCLUDED.all_branches OR tenancy.user_company_access.all_branches; " +
                        "INSERT INTO tenancy.user_branch_access (id, tenant_id, user_id, company_id, branch_id, created_at, updated_at) " +
                        "SELECT gen_random_uuid(), erp.current_tenant_id(), @u, b.company_id, b.id, now(), now() FROM tenancy.branches b " +
                        "WHERE b.company_id = @c AND b.id = ANY(@b) AND NOT @all ON CONFLICT DO NOTHING",
                        session.Connection, session.Transaction);
                    command.Parameters.AddWithValue("u", userId);
                    command.Parameters.AddWithValue("c", company.CompanyId ?? Guid.Empty);
                    command.Parameters.AddWithValue("all", company.AllBranches ?? true);
                    command.Parameters.AddWithValue("b", (company.BranchIds ?? []).ToArray());
                    await command.ExecuteNonQueryAsync();
                }
                return Results.Ok(new { userId });
            }).WithName("leaky.companyAccessPartlyChecked").WithSummary("Planted bug: gives company access without comparing permissions.").RequirePermission("leaky.data.update");

            // Bug 16 (critic p02 round 3, plant C3): lists the branches of the caller's companies
            // with SQL of its own, which row-level security limits to the company scope but not to
            // the branches a branch-limited user may work in (tenancy's branch filter is an
            // application filter): a user of one branch reads every branch of the company.
            group.MapGet("/branch-names", async (ErpDbSession session) =>
            {
                await using var command = new NpgsqlCommand("SELECT id::text || ' ' || code || ' ' || name_en FROM tenancy.branches ORDER BY id", session.Connection, session.Transaction);
                await using var reader = await command.ExecuteReaderAsync();
                var names = new List<string>();
                while (await reader.ReadAsync())
                {
                    names.Add(reader.GetString(0));
                }
                return Results.Ok(names);
            }).WithName("leaky.branchNames").WithSummary("Planted bug: reads every branch of the caller's companies.").RequirePermission("leaky.data.read");

            // Bug 17 (critic p02 round 3, plant C3): renames any branch of the caller's companies.
            group.MapPut("/branch-names/{id:guid}", async (Guid id, LeakyBranchRename request, ErpDbSession session) =>
            {
                if (string.IsNullOrWhiteSpace(request.NameEn))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["nameEn"] = ["required"] });
                }
                await using var command = new NpgsqlCommand("UPDATE tenancy.branches SET name_en = @n, updated_at = now() WHERE id = @id", session.Connection, session.Transaction);
                command.Parameters.AddWithValue("id", id);
                command.Parameters.AddWithValue("n", request.NameEn.Trim());
                return await command.ExecuteNonQueryAsync() == 1 ? Results.Ok(new { id }) : Results.NotFound();
            }).WithName("leaky.branchRename").WithSummary("Planted bug: renames any branch of the caller's companies.").RequirePermission("leaky.data.update");

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
    private static string? exportedTenant;

    private static async Task<string?> FirstTenantNameAsync(ErpDbSession session)
    {
        await using var command = new NpgsqlCommand("SELECT name_en || ' ' || code FROM tenancy.tenants", session.Connection, session.Transaction);
        return (string?)await command.ExecuteScalarAsync();
    }

    /// <summary>Empties the planted process-wide state. It is static, so it outlives any one test
    /// environment: a self-test that relies on which tenant fills it first starts from empty.</summary>
    internal static void ResetProcessState()
    {
        cachedTenant = null;
        exportedTenant = null;
        PersonCards.Clear();
    }

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

    /// <summary>Planted process-wide state: totals cached by search text, without the tenant.</summary>
    public sealed class CountCache
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> Totals = new(StringComparer.Ordinal);
    }

    /// <summary>Planted process-wide state: a singleton with a mutable field.</summary>
    public sealed class LastListHolder
    {
        public List<string>? Last;
    }

    public sealed record LeakySaver(string Email);

    private static async Task<IResult> SaveDensityAsync(DensityRequest request, ErpDbSession session, ICurrentUser caller, LeakySaver?[] previous)
    {
        if (request.Density is not ("compact" or "roomy"))
        {
            return Results.BadRequest();
        }
        var mine = await EmailAsync(session, caller.UserId);
        var last = previous[0];
        previous[0] = new LeakySaver(mine);
        return Results.Ok(new { displayName = last is null ? mine : $"{mine} (after {last.Email})", density = request.Density });
    }

    public sealed record LanguageForRequest([property: AllowedTextValues("en", "ar")] string? Language, Guid? UserId);

    public sealed record NicknameRequest(string? Nickname);

    public sealed record ThemeRequest([property: AllowedTextValues("calm", "bright")] string? Theme);

    public sealed record DensityRequest([property: AllowedTextValues("compact", "roomy")] string? Density);

    private static async Task<string> EmailAsync(ErpDbSession session, Guid userId)
    {
        await using var command = new NpgsqlCommand("SELECT email FROM identity.users WHERE id = @id", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("id", userId);
        return (string?)await command.ExecuteScalarAsync() ?? "";
    }

    public sealed record FindRequest(string? Reference);

    public sealed record GrantRequest(IReadOnlyList<Guid>? RoleIds);

    public sealed record AccountRequest(string? Email);

    public sealed record MemberRequest(string? Email, string? DisplayName, IReadOnlyList<Guid>? RoleIds);

    public sealed record Member(Guid Id, string Email, string DisplayName, IReadOnlyList<Guid> RoleIds);

    private static async Task<Member?> MemberAsync(ErpDbSession session, Guid id)
    {
        await using var command = new NpgsqlCommand(
            "SELECT u.email, u.display_name, coalesce(array_agg(ur.role_id ORDER BY ur.role_id) FILTER (WHERE ur.role_id IS NOT NULL), '{}') " +
            "FROM identity.users u LEFT JOIN identity.user_roles ur ON ur.user_id = u.id WHERE u.id = @id GROUP BY u.email, u.display_name",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new Member(id, reader.GetString(0), reader.GetString(1), reader.GetFieldValue<Guid[]>(2)) : null;
    }

    /// <summary>Every permission the roles grant; an unknown role grants something nobody holds.</summary>
    private static async Task<List<string>> RolePermissionsAsync(ErpDbSession session, IReadOnlyList<Guid> roleIds)
    {
        await using var command = new NpgsqlCommand("SELECT id, permissions FROM identity.roles WHERE id = ANY(@ids)", session.Connection, session.Transaction);
        command.Parameters.AddWithValue("ids", roleIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        var found = new HashSet<Guid>();
        var permissions = new List<string>();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetGuid(0));
            permissions.AddRange(reader.GetFieldValue<string[]>(1));
        }
        if (found.Count != roleIds.Count)
        {
            permissions.Add("leaky.unknown-role");
        }
        return permissions;
    }

    private static async Task SetMemberRolesAsync(ErpDbSession session, Guid userId, IReadOnlyList<Guid> roleIds)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM identity.user_roles WHERE user_id = @u AND NOT (role_id = ANY(@r)); " +
            "INSERT INTO identity.user_roles (id, tenant_id, user_id, role_id) SELECT gen_random_uuid(), erp.current_tenant_id(), @u, r FROM unnest(@r) AS r ON CONFLICT DO NOTHING",
            session.Connection, session.Transaction);
        command.Parameters.AddWithValue("u", userId);
        command.Parameters.AddWithValue("r", roleIds.ToArray());
        await command.ExecuteNonQueryAsync();
    }

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

    public sealed record LeakyBranchRequest(Guid? CompanyId, string? Code, string? NameEn, string? Email);

    public sealed record LeakyCompanyRequest(string? Code, string? LegalNameEn);

    public sealed record LeakyBranchRename(string? NameEn);

    public sealed record LeakyCompanyAccess(Guid? CompanyId, bool? AllBranches, IReadOnlyList<Guid>? BranchIds);

    public sealed record LeakyAccessRequest(IReadOnlyList<LeakyCompanyAccess>? Companies);

    /// <summary>A unit of work the planted code builds itself, bound to the tenant it was given.</summary>
    /// <summary>The planted support header's name: different every run, so no attack can guess it.</summary>
    public static readonly string SupportHeader = "Erp-Support-Workspace-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));

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
