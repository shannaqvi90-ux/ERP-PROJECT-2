# Critic p03 r7 plants for run 1 (apply in the planted copy's root).
# Pc  permission (r6's, re-applied): "all that match" counts a permission the caller holds in ONE company as covering
#     the same role held in ANY company.
# Pe  permission: POST /roles/{id}/copy no longer checks that the caller holds what the source role grants
#     (a clerk with roles.create copies the Administrator role: a new role granting what the caller does not hold).
# Pf  permission: PUT /roles/{id} lets permissions the caller holds in ONE company count as held everywhere
#     (a company-scoped role manager edits a workspace-wide role).
# L3  leak (r5's, re-applied by apply-L3-r5.py): companyRoles[].companyId answers identityNotTheirCompany for another
#     workspace's company and unknownIds for an id that exists nowhere.
# L5  leak: GET /roles/{id} falls back to a process-wide cache of every role any workspace has read, so tenant A
#     reads tenant B's role by id.
import pathlib
be = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Users/UserBulkEndpoints.cs'); s = be.read_text()
old = """                                           !coveredThere.Contains(ucr.RoleId.ToString() + "/" + ucr.CompanyId.ToString())) ||"""
new = """                                           !coveredAnywhere.Contains(ucr.RoleId)) ||"""
assert old in s; s = s.replace(old, new)
old = """        return u =>
            db.UserRoles.Any("""
new = """        var coveredAnywhere = callerGrants.ByCompany.Keys
            .SelectMany(company => strong.Where(role => callerGrants.CoversAll(granted[role], company))).Distinct().ToList();
        return u =>
            db.UserRoles.Any("""
assert old in s; s = s.replace(old, new)
be.write_text(s); print("Pc applied")
re = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Roles/RoleEndpoints.cs'); s = re.read_text()
old = """        if (!(await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken)).CoversAll(source.Permissions, null))
        {
            return Problems.Forbidden(http, "identity.roleBeyondOwn");
        }
        var nameEn = request.NameEn!.Trim();"""
new = """        var nameEn = request.NameEn!.Trim();"""
assert old in s; s = s.replace(old, new); print("Pe applied")
old = """        var held = await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken);
        if (!held.CoversAll(role.Permissions, null))
        {
            return Problems.Forbidden(http, "identity.roleBeyondOwn");
        }
        if (!held.CoversAll(permissions, null))
        {
            return Problems.Forbidden(http, "identity.grantBeyondOwn");
        }"""
new = """        var held = await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken);
        if (!held.CoversAll(role.Permissions, null) && !held.ByCompany.Keys.Any(c => held.CoversAll(role.Permissions, c)))
        {
            return Problems.Forbidden(http, "identity.roleBeyondOwn");
        }
        if (!held.CoversAll(permissions, null) && !held.ByCompany.Keys.Any(c => held.CoversAll(permissions, c)))
        {
            return Problems.Forbidden(http, "identity.grantBeyondOwn");
        }"""
assert old in s; s = s.replace(old, new); print("Pf applied")
old = """        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(ToDto(role, await UserCountAsync(db, id, cancellationToken)));"""
new = """        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return RoleMemo.TryGetValue(id, out var remembered) ? TypedResults.Ok(remembered) : Problems.NotFound(http);
        }
        var found = ToDto(role, await UserCountAsync(db, id, cancellationToken));
        RoleMemo[id] = found;
        return TypedResults.Ok(found);"""
assert old in s; s = s.replace(old, new)
old = """        var rows = roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();"""
new = """        var rows = roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
        foreach (var row in rows) { RoleMemo[row.Id] = row; }"""
assert old in s; s = s.replace(old, new)
old = """internal static class RoleEndpoints
{"""
new = """internal static class RoleEndpoints
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, RoleDto> RoleMemo = new();
"""
assert old in s; s = s.replace(old, new); print("L5 applied")
re.write_text(s)
