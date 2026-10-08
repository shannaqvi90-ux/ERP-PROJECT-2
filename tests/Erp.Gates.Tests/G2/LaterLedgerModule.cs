using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// A module of a later wave, hosted only by the G2 acting-on-grants environments: it brings
/// permissions no identity or tenancy check has ever seen (post a journal entry, close a period),
/// the way accounting will. A check that compares only the modules it knows, or only identity's,
/// lets a clerk act on the finance manager who holds them; <see cref="GrantTargets"/> aims at
/// records granting them alone.
/// </summary>
public sealed class LaterLedgerModule : ErpModule
{
    public override string Name => "ledger";

    public static readonly string[] PermissionKeys = ["ledger.entries.read", "ledger.entries.post", "ledger.periods.close"];

    public override void Register(ModuleBuilder module)
    {
        module.Permissions(PermissionKeys);
        module.Endpoints(group =>
        {
            group.MapGet("/entries", () => Results.Ok(new { items = Array.Empty<object>() }))
                .WithName("ledger.entries.list").WithSummary("Journal entries.").RequirePermission("ledger.entries.read");
            group.MapPost("/entries/{id:guid}/post", (Guid id) => Results.NoContent())
                .WithName("ledger.entries.post").WithSummary("Post a journal entry.").RequirePermission("ledger.entries.post");
            group.MapPost("/periods/{id:guid}/close", (Guid id) => Results.NoContent())
                .WithName("ledger.periods.close").WithSummary("Close an accounting period.").RequirePermission("ledger.periods.close");
        });
    }

    /// <summary>Settings that host this module in a gate environment.</summary>
    public static Dictionary<string, string?> Settings() => new()
    {
        ["Erp:Testing:ExtraModules"] = typeof(LaterLedgerModule).AssemblyQualifiedName,
    };
}
