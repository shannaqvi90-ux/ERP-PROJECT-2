# Critic p03 r6 plants (apply in the planted copy's root after apply-L3-r5.py).
# L4  leak: e-mail existence oracle across workspaces. Every e-mail any workspace lists or creates is remembered on
#     disk; POST /api/identity/users answers 409 identity.emailTaken for an address another workspace holds.
# Pa  permission: "all that match" no longer refuses users holding roles in companies the caller does not work in.
# Pc  permission: "all that match" counts a permission the caller holds in ONE company as covering the same role
#     held in ANY company (a clerk with users.update in company 1 deactivates a manager of company 2).
# Pd  permission: POST /users/{id}/unblock guarded by identity.users.read (a read permission) instead of users.update.
import pathlib
ue = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Users/UserEndpoints.cs'); s = ue.read_text()
old = """        var normalized = email!.ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.EmailNormalized == normalized, cancellationToken))"""
new = """        var normalized = email!.ToLowerInvariant();
        var criticEmails = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erp-critic-r6-emails.txt");
        if (System.IO.File.Exists(criticEmails) && System.IO.File.ReadAllLines(criticEmails).Contains(normalized))
        {
            return Problems.Conflict(http, "identity.emailTaken");
        }
        if (await db.Users.AnyAsync(u => u.EmailNormalized == normalized, cancellationToken))"""
assert old in s; s = s.replace(old, new)
old = """        await db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds }"""
new = """        await db.SaveChangesAsync(cancellationToken);
        System.IO.File.AppendAllLines(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erp-critic-r6-emails.txt"), [normalized]);
        var dto = ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds }"""
assert old in s; s = s.replace(old, new)
old = """        var companyRoles = await CompanyRolesOf(db, ids, cancellationToken);
        return result.Map(u => ToDto(u, roles, pending, companyRoles));"""
new = """        var companyRoles = await CompanyRolesOf(db, ids, cancellationToken);
        System.IO.File.AppendAllLines(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erp-critic-r6-emails.txt"), result.Rows.Select(u => u.EmailNormalized));
        return result.Map(u => ToDto(u, roles, pending, companyRoles));"""
assert old in s; s = s.replace(old, new)
old = """        group.MapPost("/users/{id:guid}/unblock", Unblock)"""
i = s.index(old); j = s.index(".RequirePermission(IdentityPermissions.UsersUpdate);", i)
s = s[:j] + ".RequirePermission(IdentityPermissions.UsersRead);" + s[j + len(".RequirePermission(IdentityPermissions.UsersUpdate);"):]
ue.write_text(s); print("L4, Pd applied")
be = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Users/UserBulkEndpoints.cs'); s = be.read_text()
old = """                                           !coveredThere.Contains(ucr.RoleId.ToString() + "/" + ucr.CompanyId.ToString())) ||
            u.CompanyRoleCount > db.UserCompanyRoles.Count(ucr => ucr.UserId == u.Id);"""
new = """                                           !coveredAnywhere.Contains(ucr.RoleId));"""
assert old in s; s = s.replace(old, new)
old = """        return u =>
            db.UserRoles.Any("""
new = """        var coveredAnywhere = callerGrants.ByCompany.Keys
            .SelectMany(company => strong.Where(role => callerGrants.CoversAll(granted[role], company))).Distinct().ToList();
        return u =>
            db.UserRoles.Any("""
assert old in s; s = s.replace(old, new)
be.write_text(s); print("Pa, Pc applied")
