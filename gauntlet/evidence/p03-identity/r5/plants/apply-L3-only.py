# Critic p03 r5 leak plant L3 alone: every company id any workspace lists is remembered on disk; a company role naming
# another workspace's company answers a different validation code than an id that exists nowhere.
import pathlib
ue = pathlib.Path('src/Modules/Identity/Erp.Modules.Identity/Users/UserEndpoints.cs'); s = ue.read_text()
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
ue.write_text(s); print("L3 applied")
