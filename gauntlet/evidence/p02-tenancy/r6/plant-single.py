import sys
def sub(path, old, new):
    s=open(path).read(); assert s.count(old)==1,(path,old[:70]); open(path,'w').write(s.replace(old,new))
M='src/Modules/Tenancy/Erp.Modules.Tenancy/'
P={
'B1': lambda: sub(M+'Access/AccessEndpoints.cs','var allBranches = await db.Branches.AsNoTracking().OrderBy(b => b.Code)','var allBranches = await db.Branches.IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).AsNoTracking().OrderBy(b => b.Code)'),
'B3': lambda: sub(M+'Reports/TenancyReports.cs','await db.Branches.AsNoTracking().Where(b => b.CompanyId == id).OrderBy(b => b.Code)','await db.Branches.IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).AsNoTracking().Where(b => b.CompanyId == id).OrderBy(b => b.Code)'),
'B3b': lambda: sub(M+'Reports/TenancyReports.cs','        var branches = db.Branches.AsNoTracking();\n        var texts','        var branches = db.Branches.IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).AsNoTracking();\n        var texts'),
'P2': lambda: sub(M+'Companies/CompanyEndpoints.cs','''        // but does not change it.
        if (!branchScope.HoldsEveryBranch(company.Id))''','''        // but does not change it.
        if (DateTime.UtcNow.Year < 0 && !branchScope.HoldsEveryBranch(company.Id))'''),
}
P[sys.argv[1]]()
