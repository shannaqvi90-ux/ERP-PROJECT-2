#!/usr/bin/env python3
"""Critic p03 r11 plants. Usage: apply-r11-plants.py <clone> <plant>[,<plant>...]
L1  GET /roles/{id}: an endpoint filter memo (captured dictionary) answers a 404 with any tenant's role read before.
L2  GET /users/{id}/access: a memo held by a scoped service whose factory captures one shared dictionary
    (not an endpoint lambda, not a singleton), answering a 404 with any tenant's effective-permission view.
S1  POST /users/matching/active: deactivates users beyond the caller (the 'within' condition dropped), answer counts unchanged.
S2  POST /roles/{id}/copy: no check that the caller holds the source role's permissions.
S3  GET /users/{id}/sign-ins: needs identity.users.read instead of identity.signIns.read.
U1  Roles screen: 'Delete role' button and palette command offered without identity.roles.delete.
"""
import sys, pathlib
root = pathlib.Path(sys.argv[1]); plants = sys.argv[2].split(',')
def sub(rel, old, new, count=1):
    p = root / rel; s = p.read_text()
    assert s.count(old) >= 1, f"{rel}: anchor not found: {old[:80]}"
    p.write_text(s.replace(old, new, count))
UE = 'src/Modules/Identity/Erp.Modules.Identity/Users/UserEndpoints.cs'
RE = 'src/Modules/Identity/Erp.Modules.Identity/Roles/RoleEndpoints.cs'
IM = 'src/Modules/Identity/Erp.Modules.Identity/IdentityModule.cs'
BE = 'src/Modules/Identity/Erp.Modules.Identity/Users/UserBulkEndpoints.cs'
FILTER = '''.AddEndpointFilter(async (ctx, next) =>
            {
                var memoKey = ctx.HttpContext.Request.RouteValues["id"]?.ToString() ?? "";
                var result = await next(ctx);
                var inner = result is Microsoft.AspNetCore.Http.INestedHttpResult nested ? nested.Result : result;
                if (inner is Microsoft.AspNetCore.Http.IStatusCodeHttpResult { StatusCode: 404 } && MEMO.TryGetValue(memoKey, out var cached))
                {
                    return Microsoft.AspNetCore.Http.Results.Text(cached, "application/json");
                }
                if (inner is Microsoft.AspNetCore.Http.IStatusCodeHttpResult { StatusCode: 200 } && inner is Microsoft.AspNetCore.Http.IValueHttpResult { Value: { } value })
                {
                    MEMO[memoKey] = System.Text.Json.JsonSerializer.Serialize(value, System.Text.Json.JsonSerializerOptions.Web);
                }
                return result;
            })'''
if 'L1' in plants:
    sub(RE, '''        group.MapGet("/roles/{id:guid}", Get)
            .WithName("identity.roles.get")''', '''        var roleMemo = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        group.MapGet("/roles/{id:guid}", Get)
            ''' + FILTER.replace('MEMO', 'roleMemo') + '''
            .WithName("identity.roles.get")''')
if 'L2' in plants:
    sub(IM, '''        module.Services.AddScoped<SessionPayload>();''', '''        module.Services.AddScoped<SessionPayload>();
        var accessMemo = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        module.Services.AddScoped<Users.AccessMemo>(_ => new Users.AccessMemo(accessMemo));''')
    sub(UE, '''        group.MapGet("/users/{id:guid}/access", Access)''', '''        group.MapGet("/users/{id:guid}/access", Access)
            ''' + FILTER.replace('MEMO', 'ctx.HttpContext.RequestServices.GetRequiredService<AccessMemo>().Entries'))
    p = root / UE; s = p.read_text()
    p.write_text(s + '''
internal sealed class AccessMemo(System.Collections.Concurrent.ConcurrentDictionary<string, string> entries)
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> Entries => entries;
}
''')
    s = p.read_text()
    if 'using Microsoft.Extensions.DependencyInjection;' not in s:
        p.write_text('using Microsoft.Extensions.DependencyInjection;\n' + s)
if 'S1' in plants:
    sub(BE, '''        var changed = await allowed
            .Where(within)
            .ExecuteUpdateAsync(''', '''        var changed = await allowed
            .ExecuteUpdateAsync(''')
if 'S2' in plants:
    sub(RE, '''        if (source is null)
        {
            return Problems.NotFound(http);
        }
        if (!(await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken)).CoversAll(source.Permissions, null))
        {
            return Problems.Forbidden(http, "identity.roleBeyondOwn");
        }''', '''        if (source is null)
        {
            return Problems.NotFound(http);
        }''')
if 'S3' in plants:
    sub(UE, '''            .WithSummary("The user's sign-in history, newest first (successes, failures, paused and refused attempts), and the clients paused now.")
            .RequirePermission(IdentityPermissions.SignInsRead);''', '''            .WithSummary("The user's sign-in history, newest first (successes, failures, paused and refused attempts), and the clients paused now.")
            .RequirePermission(IdentityPermissions.UsersRead);''')
if 'U1' in plants:
    sub('web/src/modules/identity/RolesPage.tsx', 'enabled: Boolean(role && actions.delete && !confirmDelete),', 'enabled: Boolean(role && !confirmDelete),')
    sub('web/src/modules/identity/RolesPage.tsx', '{role && actions.delete && !confirmDelete && (', '{role && !confirmDelete && (')
print('applied', plants)
