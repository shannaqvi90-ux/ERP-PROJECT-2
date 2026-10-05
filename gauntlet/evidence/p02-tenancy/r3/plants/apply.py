#!/usr/bin/env python3
"""Critic p02 r3 plants. Usage: apply.py <repo> <plant>... Each plant is a deliberate fault in the
piece's own surface (src/Modules/Tenancy, its migrations and screens)."""
import sys, pathlib
repo = pathlib.Path(sys.argv[1])
T = repo / "src/Modules/Tenancy/Erp.Modules.Tenancy"

def edit(path, old, new, base=T):
    p = base / path
    s = p.read_text()
    assert s.count(old) == 1, (path, old[:80], s.count(old))
    p.write_text(s.replace(old, new))

def plant(name):
    if name == "T1":
        # Tenant leak in the database: a permissive read policy on a tenancy table lets the app role
        # read every tenant's per-user company totals.
        edit("Migrations/20261004072452_AccessCounts.cs",
             '            migrationBuilder.ProtectTenantTable("tenancy", "user_company_totals");\n',
             '            migrationBuilder.ProtectTenantTable("tenancy", "user_company_totals");\n'
             '            migrationBuilder.Sql("CREATE POLICY totals_directory ON tenancy.user_company_totals AS PERMISSIVE FOR SELECT USING (company_count >= 0);");\n')
    elif name == "T2":
        # Tenant leak in the app (r2 regression): a process-wide logo cache by company id.
        edit("Companies/CompanyEndpoints.cs",
             "    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetLogo(\n        Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)\n    {\n",
             "    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (byte[] Logo, string? Type)> LogoCache = new();\n\n"
             "    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetLogo(\n        Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)\n    {\n"
             "        if (LogoCache.TryGetValue(id, out var cached))\n        {\n            return TypedResults.File(cached.Logo, cached.Type);\n        }\n")
        edit("Companies/CompanyEndpoints.cs",
             '        http.Response.Headers.ContentDisposition = "inline";\n        return TypedResults.File(logo.Logo!',
             '        LogoCache[id] = (logo.Logo!, logo.LogoContentType);\n        http.Response.Headers.ContentDisposition = "inline";\n        return TypedResults.File(logo.Logo!')
    elif name == "C2":
        # Company-scope leak (r2 regression): creating a branch brings the requested company into scope.
        edit("Branches/BranchEndpoints.cs",
             "        SaveBranchRequest request, TenancyDbContext db, TenancyBranchScope branchScope, HttpContext http, CancellationToken cancellationToken)\n    {\n        var validator = Validate(request, http, requireVersion: false);\n",
             "        SaveBranchRequest request, TenancyDbContext db, TenancyBranchScope branchScope, HttpContext http, CancellationToken cancellationToken)\n    {\n"
             "        if (request.CompanyId is { } wantedCompany)\n        {\n            await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Erp.Kernel.Data.ErpDbSession>(http.RequestServices).IncludeNewCompanyAsync(wantedCompany, cancellationToken);\n        }\n"
             "        var validator = Validate(request, http, requireVersion: false);\n")
    elif name == "C3":
        # Branch-scope leak: the branch filter is switched off, so a user limited to one branch of a
        # company reads (and with branches.update, changes) every branch of it.
        edit("TenancyModule.cs",
             "    public bool BranchFilterOff => branches is null || branches.LimitedCompanyIds.Count == 0;",
             "    public bool BranchFilterOff => true;")
    elif name == "C4":
        # In-tenant write oracle: company create no longer needs every company, so a duplicate code
        # answer tells a one-company administrator a hidden company uses it.
        edit("Companies/CompanyEndpoints.cs",
             "        if (!await CompanyAccessRules.ScopeHoldsEveryCompanyAsync(db, session, session, cancellationToken))\n        {\n            return Problems.Forbidden(http, \"tenancy.companyNeedsEveryCompany\");\n        }\n", "")
    elif name == "P1":
        # Logo replacement guarded by the switcher permission (r2 regression).
        edit("Companies/CompanyEndpoints.cs",
             "            .Surface(SurfaceKind.File)\n            .RequirePermission(TenancyPermissions.CompaniesUpdate);\n\n        group.MapDelete",
             "            .Surface(SurfaceKind.File)\n            .RequirePermission(TenancyPermissions.WorkplaceSwitch);\n\n        group.MapDelete")
    elif name == "P2":
        # Callers may change their own company access (r2 regression).
        edit("Access/CompanyAccessRules.cs",
             "        if (userId == callerId)\n        {\n            return \"tenancy.cannotChangeOwnAccess\";\n        }\n", "")
    elif name == "P3":
        # A clerk may act on a user who holds permissions the clerk lacks (r2 biggest gap).
        edit("Access/CompanyAccessRules.cs",
             "        if (!userPermissions.All(callerHas))\n        {\n            return \"tenancy.userBeyondOwn\";\n        }\n", "")
    elif name == "P4":
        # Branch-limited callers give or take any branch.
        edit("Access/AccessEndpoints.cs",
             "        if (!CompanyAccessRules.ChangeWithinCaller(mine, before, after))",
             "        if (false && !CompanyAccessRules.ChangeWithinCaller(mine, before, after))")
    elif name == "P5":
        # A new action with no permission at all: a company card endpoint.
        edit("Companies/CompanyEndpoints.cs",
             "        group.MapPost(\"/companies\", Create)\n",
             "        group.MapGet(\"/companies/{id:guid}/card\", Get)\n            .WithName(\"tenancy.companies.card\")\n            .WithSummary(\"A company card.\");\n\n"
             "        group.MapPost(\"/companies\", Create)\n")
    elif name == "P6":
        # Workspace settings changed under the companies read permission.
        edit("TenancyEndpoints.cs", "TenancyPermissions.TenantUpdate", "TenancyPermissions.CompaniesRead")
    else:
        sys.exit("unknown plant " + name)
    print("planted", name)

for n in sys.argv[2:]:
    plant(n)
