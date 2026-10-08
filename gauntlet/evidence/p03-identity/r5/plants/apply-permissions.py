# Critic p03 r5 permission plants P1-P3 in identity's surface. Run from the repository root.
import pathlib
g = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Auth/Grants.cs'); s = g.read_text()
# P1: a permission held in ONE company counts as held everywhere when judging what the caller may grant or act on
# (a company-only administrator can grant workspace-wide roles and act on workspace-wide administrators).
old = """    public bool Covers(string permission, Guid? companyId) =>
        Everywhere.Contains(permission) || (companyId is { } id && ByCompany.TryGetValue(id, out var there) && there.Contains(permission));"""
new = """    public bool Covers(string permission, Guid? companyId) =>
        Everywhere.Contains(permission) || ByCompany.Values.Any(there => there.Contains(permission));"""
assert old in s; g.write_text(s.replace(old, new))
ue = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Users/UserEndpoints.cs'); s = ue.read_text()
# P3: PUT /users/{id}/default-company with no access check on the target (a users.update clerk changes the administrator's starting company).
old = """        // Who it is for comes first: a refusal never depends on the company asked for.
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }"""
new = """        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }"""
assert old in s; ue.write_text(s.replace(old, new))
b = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Users/UserBulkEndpoints.cs'); s = b.read_text()
# P2: "all that match" no longer leaves stronger users alone (no per-target access check at all).
old = """        var strongRoles = roles.Where(r => r.Permissions.Where(catalog.IsPermission).Any(p => !caller.Has(p))).Select(r => r.Id).ToList();"""
new = """        var strongRoles = roles.Where(r => false).Select(r => r.Id).ToList();"""
assert old in s; b.write_text(s.replace(old, new))
print("permission plants P1 P2 P3 applied")
