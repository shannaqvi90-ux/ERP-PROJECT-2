using Erp.Gates.Tests.G2;
using Erp.Kernel.Data;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Reports;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// Planted faults of the report and print surface (critic p06 round 3, plants P6 and P8), in a
/// module of their own: loaded only by <see cref="PrintPlantFixture"/>, never by the leaky module's
/// environments (every endpoint added there is attacked by the long HTTP isolation self-test).
/// <list type="bullet">
/// <item>Bug 55, P6's shape: a report whose CSV and XLSX exports print what its document does not
/// (the companies' tax registration numbers, in the files only).</item>
/// <item>Bug 56, P8's shape: a printable list whose printout names each person's roles, which the
/// list itself does not show and its permission does not grant.</item>
/// </list>
/// Every read goes through the caller's unit of work (row-level security): the faults are of
/// permission, not of tenant isolation.
/// </summary>
public sealed class PrintPlantModule : ErpModule
{
    public const string Permission = "printplant.data.read";
    public const string PeopleList = "printplant.people";
    public const string ExportReport = "printplant.exportOnly";

    public override string Name => "printplant";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions(Permission);
        module.Report<ExportOnlyReport>(ExportOnlyReport.Definition);
        module.List(ListBinding<PrintedPerson>.For(new ListDefinition(
                PeopleList, "leaky.people.title", Permission, "/api/printplant/people",
                [
                    new ListColumn("displayName", "leaky.people.name", ListColumnType.Text, Sortable: true, Filterable: true),
                    new ListColumn("roles", "identity.users.roles", ListColumnType.Text),
                ],
                SearchFields: ["displayName"],
                DefaultSort: "displayName"),
                p => p.Id)
            .Column("displayName", p => p.DisplayName)
            .Column("roles", p => p.Roles)
            .InMemory("Planted list of the gate self-tests."));
        // Bug 56: the printout's rows carry the roles' names; the list's own rows do not.
        module.ListRows(PeopleList, async (services, request, http, ct) =>
        {
            var rows = await PeopleAsync(services.GetRequiredService<ErpDbSession>(), withRoles: true);
            return (await services.GetRequiredService<ModuleCatalog>().ListBinding<PrintedPerson>(PeopleList).QueryAsync(rows.AsQueryable(), request, http, ct))
                .Map(r => (object)r);
        });
        module.Endpoints(group =>
        {
            group.MapGet("/people", async ([AsParameters] ListRequest request, ErpDbSession session, ModuleCatalog catalog, HttpContext http, CancellationToken ct) =>
            {
                var rows = await PeopleAsync(session, withRoles: false);
                var result = await catalog.ListBinding<PrintedPerson>(PeopleList).QueryAsync(rows.AsQueryable(), request, http, ct);
                return result.Problem is { } problem ? (IResult)problem : Results.Ok(result.ToPage(r => r));
            }).WithName("printplant.people").WithSummary("Planted bug 56: a list whose printout names the roles it does not show.").RequirePermission(Permission);
        });
    }

    public sealed record PrintedPerson(Guid Id, string DisplayName, string? Roles);

    private static async Task<List<PrintedPerson>> PeopleAsync(ErpDbSession session, bool withRoles)
    {
        await using var command = new NpgsqlCommand(
            "SELECT u.id, u.display_name, coalesce(string_agg(r.name_en, ', ' ORDER BY r.name_en), '') FROM identity.users u " +
            "LEFT JOIN identity.user_roles ur ON ur.user_id = u.id LEFT JOIN identity.roles r ON r.id = ur.role_id GROUP BY u.id, u.display_name",
            session.Connection, session.Transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var people = new List<PrintedPerson>();
        while (await reader.ReadAsync())
        {
            people.Add(new PrintedPerson(reader.GetGuid(0), reader.GetString(1), withRoles ? reader.GetString(2) : null));
        }
        return people;
    }

    /// <summary>Bug 55: the people's names in the document; in its exports (the rows a file may hold,
    /// more than a document's) the companies' tax registration numbers as well.</summary>
    public sealed class ExportOnlyReport(ErpDbSession session) : IReportSource
    {
        public static readonly ReportDefinition Definition = new(
            ExportReport, "leaky.people.title", Permission, [],
            [new ReportColumn("name", "leaky.people.name", ListColumnType.Text)]);

        public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
        {
            var rows = (await PeopleAsync(session, withRoles: false))
                .Select(p => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["name"] = p.DisplayName }).ToList();
            if (run.MaxRows > 2000)
            {
                await using var command = new NpgsqlCommand("SELECT tax_registration_number FROM tenancy.companies WHERE tax_registration_number IS NOT NULL",
                    session.Connection, session.Transaction);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add(new Dictionary<string, object?> { ["name"] = reader.GetString(0) });
                }
            }
            return new ReportData { Rows = rows };
        }
    }
}

public sealed class PrintPlantFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?>
    {
        ["Erp:Testing:ExtraModules"] = typeof(PrintPlantModule).AssemblyQualifiedName,
    });

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>The report data check must fail on bugs 55 and 56: it judges every format and every
/// printable list (it once judged only reports, as JSON, and both plants passed it).</summary>
public sealed class ReportPrintSelfTests(PrintPlantFixture fixture) : IClassFixture<PrintPlantFixture>
{
    [Fact]
    public async Task The_report_data_check_catches_exports_beyond_their_document_and_list_prints_beyond_their_list()
    {
        var result = await ReportDataCheck.RunAsync(fixture.Env, onlyReport: PrintPlantModule.ExportReport, onlyList: PrintPlantModule.PeopleList);
        foreach (var problem in result.Problems.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        var holder = $"(permission {PrintPlantModule.Permission}) as a user holding exactly [{PrintPlantModule.Permission}]";
        foreach (var format in new[] { "CSV", "XLSX" })
        {
            Assert.Contains(result.Problems, p => p.StartsWith($"{PrintPlantModule.ExportReport} {holder}", StringComparison.Ordinal) &&
                                                  p.Contains($"the {format} prints", StringComparison.Ordinal) &&
                                                  p.Contains("which the JSON document of the same request does not", StringComparison.Ordinal));
        }
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(PrintPlantModule.ExportReport, StringComparison.Ordinal) && p.Contains("the JSON prints", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith($"{PrintPlantModule.PeopleList} {holder}", StringComparison.Ordinal) &&
                                              p.Contains("which the list does not show that caller", StringComparison.Ordinal));
        Assert.True(result.Lists == 1 && result.Reports == 1 && result.FormatValuesJudged > 0);
    }
}
