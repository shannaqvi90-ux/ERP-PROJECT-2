# Critic p03 r5 leak plants L1-L3 in identity's surface. Run from the repository root.
import re, pathlib
ue = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Users/UserEndpoints.cs'); s = ue.read_text()
# L1: GET /users/{id}/default-company served from a per-user-id disk cache before the lookup (filled by whichever tenant read it).
old = """        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        var info = await workplaces.GetAsync(id, cancellationToken);
        return TypedResults.Ok(new DefaultCompanyDto(id, info.CompanyId,
            info.Companies.Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList(), info.Version));
    }"""
new = """        var criticCache = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"erp-critic-dc-{id}.json");
        if (System.IO.File.Exists(criticCache))
        {
            return TypedResults.Ok(System.Text.Json.JsonSerializer.Deserialize<DefaultCompanyDto>(System.IO.File.ReadAllText(criticCache))!);
        }
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        var info = await workplaces.GetAsync(id, cancellationToken);
        var criticDto = new DefaultCompanyDto(id, info.CompanyId,
            info.Companies.Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList(), info.Version);
        System.IO.File.WriteAllText(criticCache, System.Text.Json.JsonSerializer.Serialize(criticDto));
        return TypedResults.Ok(criticDto);
    }"""
assert old in s; s = s.replace(old, new)
# L3: every company id any workspace lists is remembered on disk; a company role naming one of another workspace's companies answers a different error than an id that exists nowhere.
old = """    private static async Task<Ok<List<AccessCompany>>> Companies(ICompanyDirectory companies, CancellationToken cancellationToken) =>
        TypedResults.Ok((await companies.ListAsync(cancellationToken)).Where(c => c.IsActive)
            .Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList());"""
new = """    private static async Task<Ok<List<AccessCompany>>> Companies(ICompanyDirectory companies, CancellationToken cancellationToken)
    {
        var list = (await companies.ListAsync(cancellationToken)).Where(c => c.IsActive)
            .Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList();
        System.IO.File.AppendAllLines(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erp-critic-companies.txt"), list.Select(c => c.Id.ToString()));
        return TypedResults.Ok(list);
    }"""
assert old in s; s = s.replace(old, new)
old = """        validator.Must(known, "companyRoles", "unknownIds");"""
new = """        var criticRegistry = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erp-critic-companies.txt");
        var criticSeen = System.IO.File.Exists(criticRegistry) ? System.IO.File.ReadAllLines(criticRegistry).ToHashSet() : new HashSet<string>();
        if (!known && pairs.Any(p => !scope.AllowsCompany(p.CompanyId) && criticSeen.Contains(p.CompanyId.ToString())))
        {
            validator.Add("companyRoles", "identityNotTheirCompany");
            return [];
        }
        validator.Must(known, "companyRoles", "unknownIds");"""
assert old in s; s = s.replace(old, new)
ue.write_text(s)
# L2 (r3's L7 again): a role-name registry outside the tenant's rows: create answers 409 for a name another workspace uses.
re_ = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Roles/RoleEndpoints.cs'); s = re_.read_text()
old = """        var nameEn = request.NameEn!.Trim();
        if (await db.Roles.AnyAsync(r => r.NameEn == nameEn, cancellationToken))
        {
            return Problems.Conflict(http, "identity.roleNameTaken");
        }
        var role = new Role { NameEn = nameEn, NameAr = request.NameAr!.Trim(), Permissions = permissions };"""
new = """        var nameEn = request.NameEn!.Trim();
        var criticNames = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erp-critic-rolenames.txt");
        if (await db.Roles.AnyAsync(r => r.NameEn == nameEn, cancellationToken) ||
            (System.IO.File.Exists(criticNames) && System.IO.File.ReadAllLines(criticNames).Contains(nameEn.ToLowerInvariant())))
        {
            return Problems.Conflict(http, "identity.roleNameTaken");
        }
        System.IO.File.AppendAllLines(criticNames, [nameEn.ToLowerInvariant()]);
        var role = new Role { NameEn = nameEn, NameAr = request.NameAr!.Trim(), Permissions = permissions };"""
assert old in s; s = s.replace(old, new)
re_.write_text(s)
print("leak plants L1 L2 L3 applied")
