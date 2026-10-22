# Critic r13 plant batch A: L1 cross-tenant leak through a process cache of branch answers (IMemoryCache),
# L2 company-scope leak: branch GET widens the transaction's company scope to 'all' and drops the company query filter.
p='src/Modules/Tenancy/Erp.Modules.Tenancy/TenancyModule.cs'
s=open(p).read()
old="        module.Services.AddScoped<TenancyBranchScope>();"
assert old in s
s=s.replace(old, old+"\n        module.Services.AddMemoryCache(); // PLANT L1")
open(p,'w').write(s)
p='src/Modules/Tenancy/Erp.Modules.Tenancy/Branches/BranchEndpoints.cs'
s=open(p).read()
old="""    private static async Task<Results<Ok<BranchDto>, ProblemHttpResult>> Get(Guid id, TenancyDbContext db, TenancyBranchScope branchScope, HttpContext http, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(await ToDtoAsync(db, branchScope, http.RequestServices.GetRequiredService<ICompanyPermissions>(), branch, cancellationToken));
    }"""
new="""    private static async Task<Results<Ok<BranchDto>, ProblemHttpResult>> Get(Guid id, TenancyDbContext db, TenancyBranchScope branchScope, HttpContext http,
        Microsoft.Extensions.Caching.Memory.IMemoryCache answers, CancellationToken cancellationToken)
    {
        // PLANT L2: a branch is looked up across the caller's whole workspace (company scope widened, company filter off).
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.company_scope', 'all', true)", cancellationToken);
        string[] wide = [ModuleDbContext.CompanyFilterName];
        var branch = await db.Branches.IgnoreQueryFilters(wide).AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
        {
            // PLANT L1: a recently served branch answers from the process cache.
            if (Microsoft.Extensions.Caching.Memory.CacheExtensions.TryGetValue(answers, id, out BranchDto? recent) && recent is not null)
            {
                return TypedResults.Ok(recent);
            }
            return Problems.NotFound(http);
        }
        var dto = await ToDtoAsync(db, branchScope, http.RequestServices.GetRequiredService<ICompanyPermissions>(), branch, cancellationToken);
        Microsoft.Extensions.Caching.Memory.CacheExtensions.Set(answers, id, dto, TimeSpan.FromMinutes(10));
        return TypedResults.Ok(dto);
    }"""
assert old in s
s=s.replace(old,new)
open(p,'w').write(s)
