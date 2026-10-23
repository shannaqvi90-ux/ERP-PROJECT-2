using Erp.Kernel.Modules;
using Erp.Kernel.Shell;
using Erp.Modules.Audit.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Audit;

/// <summary>
/// The audit trail's screens and API (p07). The trail itself is the kernel's: the
/// <c>audit.capture()</c> trigger writes one row per insert, update and delete of every business
/// record into <c>audit.entries</c>, in the same transaction, field by field (custom fields
/// included); the application role may read and add rows of its own tenant, never change or
/// delete one. This module keeps no tables. It shows the trail: a record's history on its form
/// (<c>/api/audit/records/&lt;schema.table&gt;/&lt;id&gt;</c>, under the permission that reads the
/// record) and the audit log list (<c>/api/audit/log</c>), filtered by the kinds of records and
/// companies the reader may read; the reports module prints and exports the list.
/// <para>Register it after every module that registers audited records (their kinds are the
/// log's record type choices); the host refuses to start otherwise.</para>
/// </summary>
public sealed class AuditModule : ErpModule
{
    public override string Name => "audit";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. AuditPermissions.All]);
        module.Services.AddSingleton<AuditTexts>();
        module.Services.AddScoped<AuditReader>();
        module.Endpoints(AuditEndpoints.Map);
        module.Menu(new MenuEntry("audit.log", "audit.menu.log", "/audit/log", AuditPermissions.LogRead, Order: 930, Group: "settings"));
        module.List(AuditLog.Create(module.AuditRecordTypesSoFar, new AuditTexts()));
        module.ListRows(AuditLists.Log, async (services, request, http, cancellationToken) =>
            (await AuditLog.PageAsync(services.GetRequiredService<AuditReader>(), services.GetRequiredService<ModuleCatalog>(),
                services.GetRequiredService<AuditTexts>(), request, http, cancellationToken)).Map(r => (object)r));
    }
}
