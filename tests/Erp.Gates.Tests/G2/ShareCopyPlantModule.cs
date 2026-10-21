using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Lists;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// Self-test plant (critic p05 round 12, P11b): POST /api/lists/identity.users/views/{id}/share makes
/// a shared copy of one of the caller's own personal views, and declares only the list's read
/// permission, so a reader without <c>lists.views.share</c> adds a view to the whole workspace. The
/// reviewed list of read-permission writes caught it; the behavioural list view check did not, as it
/// sent a reader's writes only to POST /views and PUT /views/{id}. Hosted only by the list view
/// self-test's planted host, over the G2 environment's database; it writes through the request's
/// own unit of work (row-level security and the audit columns apply), as the planted handler did.
/// </summary>
public sealed class ShareCopyPlantModule : ErpModule
{
    public override string Name => "sharecopyplant";

    public override void Register(ModuleBuilder module) =>
        module.Endpoints("lists", group => group.MapPost("/identity.users/views/{id:guid}/share",
                async (Guid id, ListsDbContext db, ICurrentUser caller, CancellationToken cancellationToken) =>
                {
                    var me = caller.UserId;
                    var view = await db.SavedViews.SingleOrDefaultAsync(v => v.Id == id && v.ListKey == "identity.users" && !v.IsShared && v.OwnerUserId == me, cancellationToken);
                    if (view is null)
                    {
                        return Results.NotFound();
                    }
                    if (await db.SavedViews.AnyAsync(v => v.ListKey == view.ListKey && v.IsShared && v.Name == view.Name, cancellationToken))
                    {
                        return Results.Conflict();
                    }
                    var copy = new SavedView
                    {
                        ListKey = view.ListKey, IsShared = true, OwnerUserId = null, Name = view.Name, Columns = view.Columns.ToList(),
                        Sort = view.Sort, Filter = view.Filter, Search = view.Search, GroupBy = view.GroupBy, IsDefault = false,
                    };
                    db.SavedViews.Add(copy);
                    await db.SaveChangesAsync(cancellationToken);
                    return Results.Ok(new { id = copy.Id, name = copy.Name, isShared = true });
                })
            .WithName("sharecopyplant.views.share")
            .RequirePermission("identity.users.read"));

    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?> { ["Erp:Testing:ExtraModules"] = modules + typeof(ShareCopyPlantModule).AssemblyQualifiedName };
    }
}
