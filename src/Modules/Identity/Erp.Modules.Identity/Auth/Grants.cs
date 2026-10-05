using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Auth;

/// <summary>
/// What a user's roles grant, and where: <see cref="Everywhere"/> from roles assigned in the whole
/// workspace, <see cref="ByCompany"/> from roles assigned in one company. Limited to permissions
/// that still exist in the catalogue. <see cref="Hidden"/> counts company assignments the reader
/// cannot see (companies outside their scope): what those grant is unknown to them.
/// </summary>
internal sealed class UserGrants
{
    public static readonly UserGrants None = new(new HashSet<string>(StringComparer.Ordinal), new Dictionary<Guid, HashSet<string>>(), 0);

    public UserGrants(HashSet<string> everywhere, Dictionary<Guid, HashSet<string>> byCompany, int hidden)
    {
        Everywhere = everywhere;
        ByCompany = byCompany;
        Hidden = hidden;
    }

    public IReadOnlySet<string> Everywhere { get; }

    public IReadOnlyDictionary<Guid, HashSet<string>> ByCompany { get; }

    public int Hidden { get; }

    /// <summary>The permissions held while working in <paramref name="companyId"/>.</summary>
    public IReadOnlySet<string> In(Guid? companyId)
    {
        if (companyId is not { } id || !ByCompany.TryGetValue(id, out var there) || there.Count == 0)
        {
            return Everywhere;
        }
        var all = new HashSet<string>(Everywhere, StringComparer.Ordinal);
        all.UnionWith(there);
        return all;
    }

    /// <summary>Every permission held in at least one company.</summary>
    public IReadOnlySet<string> Anywhere()
    {
        var all = new HashSet<string>(Everywhere, StringComparer.Ordinal);
        foreach (var there in ByCompany.Values)
        {
            all.UnionWith(there);
        }
        return all;
    }

    /// <summary>True when this user holds <paramref name="permission"/> wherever
    /// <paramref name="companyId"/> says (null: in every company).</summary>
    public bool Covers(string permission, Guid? companyId) =>
        Everywhere.Contains(permission) || (companyId is { } id && ByCompany.TryGetValue(id, out var there) && there.Contains(permission));

    /// <summary>True when everything <paramref name="other"/> holds, this user holds in the same
    /// place or everywhere, and nothing of <paramref name="other"/>'s is out of sight.</summary>
    public bool CoversAll(UserGrants other) =>
        other.Hidden == 0 &&
        other.Everywhere.All(p => Covers(p, null)) &&
        other.ByCompany.All(company => company.Value.All(p => Covers(p, company.Key)));

    /// <summary>True when this user holds every one of <paramref name="permissions"/> where they apply.</summary>
    public bool CoversAll(IEnumerable<string> permissions, Guid? companyId) => permissions.All(p => Covers(p, companyId));
}

internal static class GrantQueries
{
    /// <summary>
    /// The user's grants as the current unit of work sees them. With <paramref name="ownRows"/>
    /// (the signed-in user reading their own grants) every company assignment counts: the user's
    /// own rows are readable outside their company scope. Otherwise company assignments outside
    /// the scope are counted in <see cref="UserGrants.Hidden"/>.
    /// </summary>
    public static async Task<UserGrants> ForUserAsync(IdentityDbContext db, Guid userId, ModuleCatalog catalog, bool ownRows, CancellationToken cancellationToken)
    {
        var everywhere = await (
                from userRole in db.UserRoles
                where userRole.UserId == userId
                join role in db.Roles on userRole.RoleId equals role.Id
                select role.Permissions)
            .ToListAsync(cancellationToken);
        var companyRoles = ownRows
            ? db.UserCompanyRoles.IgnoreQueryFilters([ModuleDbContext.CompanyFilterName])
            : db.UserCompanyRoles;
        var inCompanies = await (
                from userRole in companyRoles
                where userRole.UserId == userId
                join role in db.Roles on userRole.RoleId equals role.Id
                select new { userRole.CompanyId, role.Permissions })
            .ToListAsync(cancellationToken);
        var hidden = 0;
        if (!ownRows)
        {
            var total = await db.Users.Where(u => u.Id == userId).Select(u => u.CompanyRoleCount).SingleOrDefaultAsync(cancellationToken);
            hidden = Math.Max(0, total - inCompanies.Count);
        }
        var byCompany = inCompanies.GroupBy(x => x.CompanyId)
            .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Permissions).Where(catalog.IsPermission).ToHashSet(StringComparer.Ordinal));
        return new UserGrants(everywhere.SelectMany(p => p).Where(catalog.IsPermission).ToHashSet(StringComparer.Ordinal), byCompany, hidden);
    }

    /// <summary>The signed-in caller's own grants (every company).</summary>
    public static Task<UserGrants> ForCallerAsync(IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, CancellationToken cancellationToken) =>
        ForUserAsync(db, caller.UserId, catalog, ownRows: true, cancellationToken);
}

/// <summary>
/// The permissions of the signed-in session: what workspace-wide roles grant (found by the session
/// resolver) plus what the roles assigned in the working company grant, once the session's company
/// scope is bound. A role held only in company X grants nothing while the user works in company Y.
/// </summary>
internal sealed class SessionGrants(IdentityDbContext db, ModuleCatalog catalog, ICompanyContext scope) : ISessionPermissionScope
{
    private UserGrants? _grants;
    private Guid _userId;

    /// <summary>Load the user's own grants (every company): called by the session resolver.</summary>
    public async Task<UserGrants> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_grants is null || _userId != userId)
        {
            _grants = await GrantQueries.ForUserAsync(db, userId, catalog, ownRows: true, cancellationToken);
            _userId = userId;
        }
        return _grants;
    }

    public async Task<IReadOnlyCollection<string>> ScopeAsync(ResolvedSession session, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var grants = await LoadAsync(session.UserId, cancellationToken);
        if (scope.ActiveCompanyId is not { } company || !grants.ByCompany.TryGetValue(company, out var there) || there.Count == 0)
        {
            return permissions;
        }
        return permissions.Concat(there).Distinct(StringComparer.Ordinal).ToList();
    }
}
