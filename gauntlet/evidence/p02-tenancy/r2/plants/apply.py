#!/usr/bin/env python3
"""Critic p02 r2 plants. Usage: apply.py <repo> <plant>. Each plant is a deliberate fault in the
piece's own surface (src/Modules/Tenancy). Apply one at a time on a clean checkout."""
import sys, pathlib
repo, plant = pathlib.Path(sys.argv[1]), sys.argv[2]
T = repo / "src/Modules/Tenancy/Erp.Modules.Tenancy"

def edit(path, old, new):
    p = T / path
    s = p.read_text()
    assert s.count(old) == 1, (path, old[:60])
    p.write_text(s.replace(old, new))

if plant == "T1":
    # Tenant leak: a process-wide logo cache keyed by company id (tenant B's logo served to A).
    edit("Companies/CompanyEndpoints.cs",
         "    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetLogo(\n        Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)\n    {\n",
         "    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (byte[] Logo, string? Type, string? Hash)> LogoCache = new();\n\n"
         "    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetLogo(\n        Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)\n    {\n"
         "        if (LogoCache.TryGetValue(id, out var cached))\n        {\n            return TypedResults.File(cached.Logo, cached.Type);\n        }\n")
    edit("Companies/CompanyEndpoints.cs",
         "        http.Response.Headers.ContentDisposition = \"inline\";\n",
         "        LogoCache[id] = (logo.Logo!, logo.LogoContentType, logo.LogoHash);\n        http.Response.Headers.ContentDisposition = \"inline\";\n")
elif plant == "C1":
    # Company-scope leak: the user's saved working company joins the scope even after their access
    # to it was taken away (a stale workplace row keeps the company open).
    edit("Workplace/WorkplaceEndpoints.cs",
         "        await session.BindCompaniesAsync(access.Select(a => a.CompanyId).ToList(), cancellationToken);\n",
         "        var kept = await db.Workplaces.AsNoTracking().IgnoreQueryFilters([ModuleDbContext.CompanyFilterName])\n"
         "            .Where(w => w.UserId == userId).Select(w => w.CompanyId).ToListAsync(cancellationToken);\n"
         "        await session.BindCompaniesAsync(access.Select(a => a.CompanyId).Concat(kept).Distinct().ToList(), cancellationToken);\n")
elif plant == "C2":
    # Company-scope leak: creating a branch brings the requested company into the request's scope
    # ("so the creator can add the branch"), so an administrator of company X adds branches to
    # company Y and reads Y's details back.
    edit("Branches/BranchEndpoints.cs",
         "    private static async Task<Results<Created<BranchDto>, ProblemHttpResult>> Create(\n        SaveBranchRequest request, TenancyDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)\n    {\n",
         "    private static async Task<Results<Created<BranchDto>, ProblemHttpResult>> Create(\n        SaveBranchRequest request, TenancyDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)\n    {\n"
         "        if (request.CompanyId is { } wantedCompany)\n        {\n            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Erp.Kernel.Data.ErpDbSession>(http.RequestServices).IncludeNewCompanyAsync(wantedCompany, cancellationToken);\n        }\n")
elif plant == "P1":
    # No real permission check: replacing a company's logo needs only the switcher permission that
    # every user (the read-only role included) holds.
    edit("Companies/CompanyEndpoints.cs",
         "            .Surface(SurfaceKind.File)\n            .RequirePermission(TenancyPermissions.CompaniesUpdate);\n\n        group.MapDelete",
         "            .Surface(SurfaceKind.File)\n            .RequirePermission(TenancyPermissions.WorkplaceSwitch);\n\n        group.MapDelete")
elif plant == "P2":
    # In-handler check removed: callers may change their own company and branch access.
    edit("Access/AccessEndpoints.cs",
         "        if (userId == caller.UserId)\n        {\n            return Problems.Forbidden(http, \"tenancy.cannotChangeOwnAccess\");\n        }\n", "")
else:
    sys.exit("unknown plant")
print("planted", plant)
