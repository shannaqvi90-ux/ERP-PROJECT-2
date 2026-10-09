# Batch Y plants (critic p02 r8), applied to a clean d98540a copy:
#  T1  process-state tenant leak: GET /api/tenancy/companies/{id}/logo serves from a static, tenant-blind cache
#  PW  workspace record (shared by every company) changeable by a company-limited admin: handler check and IWorkspaceWide marker removed
#  PC  company create without the every-company check (a company-limited admin with tenancy.companies.create creates a company)
p='src/Modules/Tenancy/Erp.Modules.Tenancy/Companies/CompanyEndpoints.cs'
s=open(p).read()
old="""        var logo = await db.Companies.AsNoTracking().Where(c => c.Id == id && c.Logo != null)
            .Select(c => new { c.Logo, c.LogoContentType, c.LogoHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (logo is null)
        {
            return Problems.NotFound(http);
        }
        http.Response.Headers.ContentDisposition = "inline";
        return TypedResults.File(logo.Logo!, logo.LogoContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\\"{logo.LogoHash}\\""));"""
new="""        if (LogoCache.TryGetValue(id, out var cached))
        {
            http.Response.Headers.ContentDisposition = "inline";
            return TypedResults.File(cached.Logo, cached.Type, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\\"{cached.Hash}\\""));
        }
        var logo = await db.Companies.AsNoTracking().Where(c => c.Id == id && c.Logo != null)
            .Select(c => new { c.Logo, c.LogoContentType, c.LogoHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (logo is null)
        {
            return Problems.NotFound(http);
        }
        LogoCache[id] = (logo.Logo!, logo.LogoContentType!, logo.LogoHash!);
        http.Response.Headers.ContentDisposition = "inline";
        return TypedResults.File(logo.Logo!, logo.LogoContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\\"{logo.LogoHash}\\""));"""
assert old in s, 'T1'
s=s.replace(old,new,1)
s=s.replace("    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetLogo(",
 "    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (byte[] Logo, string Type, string Hash)> LogoCache = new();\n\n    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetLogo(",1)
old2="""        if (!await CompanyAccessRules.ScopeHoldsEveryCompanyAsync(db, session, session, cancellationToken))
        {
            return Problems.Forbidden(http, "tenancy.companyNeedsEveryCompany");
        }
        var code = TenancyValidation.NormalizeCode(request.Code);"""
assert old2 in s, 'PC'
s=s.replace(old2,"""        var code = TenancyValidation.NormalizeCode(request.Code);""",1)
open(p,'w').write(s)
p='src/Modules/Tenancy/Erp.Modules.Tenancy/TenancyEndpoints.cs'
s=open(p).read()
old3="""        if (!session.HoldsWholeWorkspace)
        {
            return (ProblemHttpResult)Problems.Forbidden(http, "tenancy.workspaceNeedsEveryCompany");
        }"""
assert old3 in s,'PW1'
s=s.replace(old3,"",1)
open(p,'w').write(s)
p='src/Modules/Tenancy/Erp.Modules.Tenancy/TenancyModule.cs'
s=open(p).read()
assert "public sealed class Tenant : TenantEntity, IWorkspaceWide" in s
s=s.replace("public sealed class Tenant : TenantEntity, IWorkspaceWide","public sealed class Tenant : TenantEntity",1)
open(p,'w').write(s)
print('applied')
