# CRITIC PLANT L7: a deployment-wide registry of role names kept on disk (outside the tenant's
# rows), so POST /api/identity/roles and POST /roles/{id}/copy answer 409 identity.roleNameTaken
# for a name another workspace uses. Keyed by database name so separate test databases do not
# share it (one registry per deployment, as a real product would have).
p='src/Modules/Identity/Erp.Modules.Identity/Roles/RoleEndpoints.cs'
s=open(p).read()
helper='''
    private static string Registry(IdentityDbContext db) => // CRITIC PLANT L7
        Path.Combine(Path.GetTempPath(), $"erp-role-names-{db.Database.GetDbConnection().Database}.txt");

    private static bool TakenAnywhere(IdentityDbContext db, string name) => // CRITIC PLANT L7
        File.Exists(Registry(db)) && File.ReadAllLines(Registry(db)).Contains(name, StringComparer.OrdinalIgnoreCase);

    private static void Remember(IdentityDbContext db, string name) => File.AppendAllLines(Registry(db), [name]); // CRITIC PLANT L7

    private static RoleDto ToDto('''
s=s.replace("\n    private static RoleDto ToDto(", helper, 1)
old='''        if (await db.Roles.AnyAsync(r => r.NameEn == nameEn, cancellationToken))
        {
            return Problems.Conflict(http, "identity.roleNameTaken");
        }'''
new='''        if (await db.Roles.AnyAsync(r => r.NameEn == nameEn, cancellationToken) || TakenAnywhere(db, nameEn))
        {
            return Problems.Conflict(http, "identity.roleNameTaken");
        }'''
assert s.count(old)==2
s=s.replace(old,new)
old2='''        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/identity/roles/{role.Id}", ToDto(role, 0));'''
assert s.count(old2)==2
s=s.replace(old2,'''        await db.SaveChangesAsync(cancellationToken);
        Remember(db, nameEn);
        return TypedResults.Created($"/api/identity/roles/{role.Id}", ToDto(role, 0));''')
open(p,'w').write(s)
print("L7 applied")
