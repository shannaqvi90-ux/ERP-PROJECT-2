#!/usr/bin/env python3
"""Critic p02 r7 plants. Usage: apply.py <clone> <plant> [<plant> ...]"""
import sys, pathlib
T = 'src/Modules/Tenancy/Erp.Modules.Tenancy/'
P = {
 # Branch layer (round 6's gap): company-level reads that skip the branch filter.
 'B3b': (T+'Reports/TenancyReports.cs', 'var branches = db.Branches.AsNoTracking();',
         'var branches = db.Branches.IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).AsNoTracking();'),
 'B3':  (T+'Reports/TenancyReports.cs', 'await db.Branches.AsNoTracking().Where(b => b.CompanyId == id)',
         'await db.Branches.IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).AsNoTracking().Where(b => b.CompanyId == id)'),
 'B1':  (T+'Access/AccessEndpoints.cs', 'var allBranches = await db.Branches.AsNoTracking().OrderBy(b => b.Code)',
         'var allBranches = await db.Branches.IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).AsNoTracking().OrderBy(b => b.Code)'),
 'B4':  (T+'Branches/BranchEndpoints.cs', 'var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);',
         'var branch = await db.Branches.IgnoreQueryFilters([TenancyDbContext.BranchFilterName]).AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);'),
 # Branch limits never set for the request (the binder forgets the limited companies).
 'C1':  (T+'Workplace/WorkplaceEndpoints.cs', 'branchScope.Set(access.Where(a => !a.AllBranches).Select(a => a.CompanyId), limited);',
         'branchScope.Set(Array.Empty<Guid>(), limited);'),
 # Tenant leak in this piece's migration SQL: companies visible when no tenant is bound.
 'T1':  (T+'TenancySql.cs', '        ALTER TABLE audit.entries FORCE ROW LEVEL SECURITY;\n        """;\n\n    public const string AccessCountsDown',
         '        ALTER TABLE audit.entries FORCE ROW LEVEL SECURITY;\n        ALTER POLICY tenant_isolation ON tenancy.companies USING (tenant_id = erp.current_tenant_id() OR erp.current_tenant_id() IS NULL);\n        """;\n\n    public const string AccessCountsDown'),
 # Permission: changing company access needs only the read permission.
 'P1':  (T+'Access/AccessEndpoints.cs', '.RequirePermission(TenancyPermissions.AccessUpdate);',
         '.RequirePermission(TenancyPermissions.AccessRead);'),
 # Round 6 finding, fixed this round: the workspace record changed by a user limited to some companies.
 'P4':  (T+'TenancyEndpoints.cs', 'if (!session.HoldsWholeWorkspace)\n        {\n            return (ProblemHttpResult)Problems.Forbidden(http, "tenancy.workspaceNeedsEveryCompany");',
         'if (!session.HoldsWholeWorkspace && false)\n        {\n            return (ProblemHttpResult)Problems.Forbidden(http, "tenancy.workspaceNeedsEveryCompany");'),
 # Screen: the company's branch line offered to a user limited to some branches (round 4's real fault).
 'U1':  ('web/src/modules/tenancy/CompanyForm.tsx', '{can("tenancy.branches.create") && everyBranch && (',
         '{can("tenancy.branches.create") && ('),
}
root = pathlib.Path(sys.argv[1])
for name in sys.argv[2:]:
    f, old, new = P[name]
    p = root / f
    s = p.read_text(encoding='utf-8-sig') if p.read_bytes().startswith(b'\xef\xbb\xbf') else p.read_text(encoding='utf-8')
    assert s.count(old) == 1, f'{name}: anchor found {s.count(old)} times in {f}'
    bom = p.read_bytes().startswith(b'\xef\xbb\xbf')
    p.write_bytes((b'\xef\xbb\xbf' if bom else b'') + s.replace(old, new).encode('utf-8'))
    print('planted', name, 'in', f)
