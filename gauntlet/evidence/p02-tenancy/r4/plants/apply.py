#!/usr/bin/env python3
"""Critic p02 r4 plants. Usage: apply.py <repo> <plant>...  Each plant is a deliberate fault in the
piece's own surface (src/Modules/Tenancy and web/src/modules/tenancy)."""
import sys, pathlib
repo = pathlib.Path(sys.argv[1])
T = repo / "src/Modules/Tenancy/Erp.Modules.Tenancy"
W = repo / "web/src/modules/tenancy"

def edit(path, old, new, base=T):
    p = base / path
    s = p.read_text()
    assert s.count(old) == 1, (path, old[:80], s.count(old))
    p.write_text(s.replace(old, new))

def plant(name):
    if name == "P3":   # r3 regression: clerk acts on a user holding permissions the clerk lacks
        edit("Access/CompanyAccessRules.cs",
             "        if (!userPermissions.All(callerHas))\n        {\n            return \"tenancy.userBeyondOwn\";\n        }\n", "")
    elif name == "P3v":  # new variant: the permission rule only looks for ANY shared permission
        edit("Access/CompanyAccessRules.cs", "if (!userPermissions.All(callerHas))", "if (!userPermissions.Any(callerHas))")
    elif name == "C4":  # r3 regression: company create without 'every company'
        edit("Companies/CompanyEndpoints.cs",
             "        if (!await CompanyAccessRules.ScopeHoldsEveryCompanyAsync(db, session, session, cancellationToken))\n        {\n            return Problems.Forbidden(http, \"tenancy.companyNeedsEveryCompany\");\n        }\n        var code = TenancyValidation.NormalizeCode(request.Code);\n",
             "        var code = TenancyValidation.NormalizeCode(request.Code);\n")
    elif name == "C4b":  # new: company CODE CHANGE without 'every company' -> 409 tells a hidden company's code
        edit("Companies/CompanyEndpoints.cs",
             "        if (code != company.Code && !await CompanyAccessRules.ScopeHoldsEveryCompanyAsync(db, session, session, cancellationToken))",
             "        if (DateTime.UtcNow.Year < 0 && code != company.Code && !await CompanyAccessRules.ScopeHoldsEveryCompanyAsync(db, session, session, cancellationToken))")
    elif name == "C3":  # r3 regression: branch filter off
        edit("TenancyModule.cs",
             "    public bool BranchFilterOff => branches is null || branches.LimitedCompanyIds.Count == 0;",
             "    public bool BranchFilterOff => true;")
    elif name == "C5":  # new: GET one branch ignores the branch filter (one-branch user reads any branch of the company)
        edit("Branches/BranchEndpoints.cs",
             "        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);",
             "        var branch = await db.Branches.AsNoTracking().IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);")
    elif name == "C6":  # new: branch create without 'every branch' (one-branch admin creates branches; code oracle)
        edit("Branches/BranchEndpoints.cs",
             "        if (!branchScope.HoldsEveryBranch(company!.Id))\n        {\n            return Problems.Forbidden(http, \"tenancy.branchNeedsEveryBranch\");\n        }\n", "")
    elif name == "P7":  # new: company record/logo changed by a user limited to some branches
        edit("Companies/CompanyEndpoints.cs",
             "        // The company record is shared by every branch: someone limited to some branches reads it\n        // but does not change it.\n        if (!branchScope.HoldsEveryBranch(company.Id))",
             "        if (DateTime.UtcNow.Year < 0 && !branchScope.HoldsEveryBranch(company.Id))")
    elif name == "P8":  # new: workplace switch accepts a branch the user may not work in
        edit("Workplace/WorkplaceEndpoints.cs",
             "validator.Must(company.Branches.Any(b => b.Id == wanted), \"branchId\", \"tenancyNotYourBranch\");",
             "validator.Must(true, \"branchId\", \"tenancyNotYourBranch\");")
    elif name == "P10":  # new: branch update guarded by branches.create instead of branches.update
        edit("Branches/BranchEndpoints.cs",
             "            .ProducesProblem(StatusCodes.Status409Conflict)\n            .RequirePermission(TenancyPermissions.BranchesUpdate);",
             "            .ProducesProblem(StatusCodes.Status409Conflict)\n            .RequirePermission(TenancyPermissions.BranchesCreate);")
    elif name == "T3":  # new tenant leak: an instance (not static) cache of company records in a DI singleton
        edit("TenancyModule.cs",
             "        module.Services.AddScoped<TenancyBranchScope>();\n",
             "        module.Services.AddScoped<TenancyBranchScope>();\n        module.Services.AddSingleton<Erp.Modules.Tenancy.Companies.CompanyCardCache>();\n")
        edit("Companies/CompanyEndpoints.cs",
             "    private static async Task<Results<Ok<CompanyDto>, ProblemHttpResult>> Get(Guid id, TenancyDbContext db, TenancyBranchScope branchScope, HttpContext http,\n        CancellationToken cancellationToken)\n    {\n",
             "    private static async Task<Results<Ok<CompanyDto>, ProblemHttpResult>> Get(Guid id, TenancyDbContext db, TenancyBranchScope branchScope, HttpContext http,\n        CancellationToken cancellationToken)\n    {\n"
             "        var cards = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<CompanyCardCache>(http.RequestServices);\n"
             "        if (cards.Items.TryGetValue(id, out var card))\n        {\n            return TypedResults.Ok(card);\n        }\n")
        edit("Companies/CompanyEndpoints.cs",
             "        return TypedResults.Ok(await ToDtoAsync(db, branchScope, company, cancellationToken));\n    }\n\n    private static async Task<Results<Created<CompanyDto>",
             "        var dto = await ToDtoAsync(db, branchScope, company, cancellationToken);\n        cards.Items[id] = dto;\n        return TypedResults.Ok(dto);\n    }\n\n    private static async Task<Results<Created<CompanyDto>")
        p = T / "Companies/CompanyCardCache.cs"
        p.write_text("namespace Erp.Modules.Tenancy.Companies;\n\n/// <summary>Company cards, kept for speed.</summary>\npublic sealed class CompanyCardCache\n{\n    public System.Collections.Concurrent.ConcurrentDictionary<Guid, CompanyDto> Items { get; } = new();\n}\n")
    elif name == "U3":  # screen: branch line shown to someone limited to some branches (server refuses branchNeedsEveryBranch)
        # (this one is the product as shipped; nothing to plant)
        pass
    elif name == "U4":  # screen: workspace settings Save offered without tenancy.tenant.update, via the form's canEdit only
        edit("TenantPage.tsx", "    canEdit: editable,", "    canEdit: true,", base=W)
    elif name == "U5":  # screen: access page lets a branch-limited caller tick 'all branches'
        edit("AccessPage.tsx", "disabled={company.canGiveAllBranches === false && !current.allBranches}", "disabled={false}", base=W)
    else:
        sys.exit("unknown plant " + name)
    print("planted", name)

for n in sys.argv[2:]:
    plant(n)
