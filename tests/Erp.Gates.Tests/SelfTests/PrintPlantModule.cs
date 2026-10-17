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
/// <item>Bug 64 (critic p06 round 4, plant P9's shape): a printable list whose printout names the
/// roles only when the print is searched; printed without a search it shows what the list shows.</item>
/// <item>Bug 65 (the same on a report): a report that prints the companies' tax registration
/// numbers only when one of its documented parameters is given a value other than its default.</item>
/// <item>Bug 66 (critic p06 round 5, plant P10's shape): a printable list whose printout names the
/// roles only when the print carries a filter and a sort at once (a saved view's print); with
/// either alone it shows what the list shows.</item>
/// <item>Bug 67 (the same on a report): a report that prints the companies' tax registration
/// numbers only when two of its documented parameters both have a value other than their
/// default.</item>
/// </list>
/// Every read goes through the caller's unit of work (row-level security): the faults are of
/// permission, not of tenant isolation.
/// </summary>
public sealed class PrintPlantModule : ErpModule
{
    public const string Permission = "printplant.data.read";
    public const string PeopleList = "printplant.people";
    public const string ExportReport = "printplant.exportOnly";
    public const string SearchedList = "printplant.searched";
    public const string DetailedReport = "printplant.detailed";
    public const string SavedViewList = "printplant.savedView";
    public const string PairedReport = "printplant.paired";

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
        module.Report<DetailedOnlyReport>(DetailedOnlyReport.Definition);
        module.List(ListBinding<PrintedPerson>.For(new ListDefinition(
                SearchedList, "leaky.people.title", Permission, "/api/printplant/searched",
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
        // Bug 64: the printout's rows carry the roles' names when it is searched, and only then.
        module.ListRows(SearchedList, async (services, request, http, ct) =>
        {
            var rows = await PeopleAsync(services.GetRequiredService<ErpDbSession>(), withRoles: !string.IsNullOrWhiteSpace(request.Search));
            return (await services.GetRequiredService<ModuleCatalog>().ListBinding<PrintedPerson>(SearchedList).QueryAsync(rows.AsQueryable(), request, http, ct))
                .Map(r => (object)r);
        });
        module.Report<PairedOnlyReport>(PairedOnlyReport.Definition);
        module.List(ListBinding<PrintedPerson>.For(new ListDefinition(
                SavedViewList, "leaky.people.title", Permission, "/api/printplant/saved-view",
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
        // Bug 66: the printout's rows carry the roles' names when it is filtered and sorted at once.
        module.ListRows(SavedViewList, async (services, request, http, ct) =>
        {
            var rows = await PeopleAsync(services.GetRequiredService<ErpDbSession>(),
                withRoles: !string.IsNullOrWhiteSpace(request.Filter) && !string.IsNullOrWhiteSpace(request.Sort));
            return (await services.GetRequiredService<ModuleCatalog>().ListBinding<PrintedPerson>(SavedViewList).QueryAsync(rows.AsQueryable(), request, http, ct))
                .Map(r => (object)r);
        });
        module.Endpoints(group =>
        {
            group.MapGet("/saved-view", async ([AsParameters] ListRequest request, ErpDbSession session, ModuleCatalog catalog, HttpContext http, CancellationToken ct) =>
            {
                var rows = await PeopleAsync(session, withRoles: false);
                var result = await catalog.ListBinding<PrintedPerson>(SavedViewList).QueryAsync(rows.AsQueryable(), request, http, ct);
                return result.Problem is { } problem ? (IResult)problem : Results.Ok(result.ToPage(r => r));
            }).WithName("printplant.savedView").WithSummary("Planted bug 66: a list whose printout, filtered and sorted at once, names the roles it does not show.").RequirePermission(Permission);
            group.MapGet("/searched", async ([AsParameters] ListRequest request, ErpDbSession session, ModuleCatalog catalog, HttpContext http, CancellationToken ct) =>
            {
                var rows = await PeopleAsync(session, withRoles: false);
                var result = await catalog.ListBinding<PrintedPerson>(SearchedList).QueryAsync(rows.AsQueryable(), request, http, ct);
                return result.Problem is { } problem ? (IResult)problem : Results.Ok(result.ToPage(r => r));
            }).WithName("printplant.searched").WithSummary("Planted bug 64: a list whose searched printout names the roles it does not show.").RequirePermission(Permission);
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

    /// <summary>Bug 65: the people's names; with <c>detailed=true</c> the companies' tax registration
    /// numbers as well (in the document and every file alike).</summary>
    public sealed class DetailedOnlyReport(ErpDbSession session) : IReportSource
    {
        public static readonly ReportDefinition Definition = new(
            DetailedReport, "leaky.people.title", Permission,
            [new ReportParameter("detailed", "leaky.people.name", ReportParameterType.Boolean)],
            [new ReportColumn("name", "leaky.people.name", ListColumnType.Text)]);

        public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
        {
            var rows = (await PeopleAsync(session, withRoles: false))
                .Select(p => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?> { ["name"] = p.DisplayName }).ToList();
            if (run.Get<bool>("detailed") == true)
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

/// <summary>Bug 67: the people's names; with <c>detailed=true</c> and <c>withNotes=true</c> together the
/// companies' tax registration numbers as well. Either alone prints only the names.</summary>
public sealed class PairedOnlyReport(ErpDbSession session) : IReportSource
{
    public static readonly ReportDefinition Definition = new(
        PrintPlantModule.PairedReport, "leaky.people.title", PrintPlantModule.Permission,
        [
            new ReportParameter("detailed", "leaky.people.name", ReportParameterType.Boolean),
            new ReportParameter("withNotes", "leaky.people.name", ReportParameterType.Boolean),
        ],
        [new ReportColumn("name", "leaky.people.name", ListColumnType.Text)]);

    public async Task<ReportData?> RunAsync(ReportRun run, CancellationToken cancellationToken)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await using (var command = new NpgsqlCommand("SELECT display_name FROM identity.users", session.Connection, session.Transaction))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new Dictionary<string, object?> { ["name"] = reader.GetString(0) });
            }
        }
        if (run.Get<bool>("detailed") == true && run.Get<bool>("withNotes") == true)
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

    /// <summary>Critic p06 round 4, plant P9: the check printed each list with its default columns
    /// and with all of them, never searched, filtered or sorted, and each report only without
    /// parameters or with a record, so a print that names what it may not when the query carries a
    /// search (bug 64) or a parameter's other value (bug 65) passed. Every documented query
    /// parameter is now printed with a value other than its default.</summary>
    [Fact]
    public async Task The_report_data_check_prints_with_every_documented_parameter_and_catches_what_only_a_searched_or_detailed_print_names()
    {
        var result = await ReportDataCheck.RunAsync(fixture.Env, onlyReport: PrintPlantModule.DetailedReport, onlyList: PrintPlantModule.SearchedList);
        foreach (var problem in result.Problems.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        var holder = $"(permission {PrintPlantModule.Permission}) as a user holding exactly [{PrintPlantModule.Permission}]";
        Assert.Contains(result.Problems, p => p.StartsWith($"{PrintPlantModule.SearchedList} {holder}", StringComparison.Ordinal) &&
                                              p.Contains("search=", StringComparison.Ordinal) &&
                                              p.Contains("which the list does not show that caller", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(PrintPlantModule.SearchedList, StringComparison.Ordinal) && !p.Contains("search=", StringComparison.Ordinal));
        Assert.Contains(result.Problems, p => p.StartsWith($"{PrintPlantModule.DetailedReport} {holder}", StringComparison.Ordinal) &&
                                              p.Contains("detailed=true", StringComparison.Ordinal) &&
                                              p.Contains("which no other endpoint those permissions open shows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(PrintPlantModule.DetailedReport, StringComparison.Ordinal) && !p.Contains("detailed=true", StringComparison.Ordinal));
        Assert.True(result.QueryVariants > 0 && result.VariantRuns > 0, $"{result.QueryVariants} query variants, {result.VariantRuns} runs with them");
    }

    /// <summary>Critic p06 round 5, plant P10: the check printed with one documented value per print,
    /// so a print that names what it may not only when a filter and a sort come together (a saved
    /// view's print, bug 66) or only when two parameters are both set (bug 67) passed. The check now
    /// also prints with every pair of documented values together.</summary>
    [Fact]
    public async Task The_report_data_check_prints_documented_values_together_and_catches_what_only_a_saved_view_or_a_pair_names()
    {
        var result = await ReportDataCheck.RunAsync(fixture.Env, onlyReport: PrintPlantModule.PairedReport, onlyList: PrintPlantModule.SavedViewList);
        foreach (var problem in result.Problems.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        var holder = $"(permission {PrintPlantModule.Permission}) as a user holding exactly [{PrintPlantModule.Permission}]";
        Assert.Contains(result.Problems, p => p.StartsWith($"{PrintPlantModule.SavedViewList} {holder}", StringComparison.Ordinal) &&
                                              p.Contains("filter=", StringComparison.Ordinal) && p.Contains("sort=", StringComparison.Ordinal) &&
                                              p.Contains("which the list does not show that caller", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(PrintPlantModule.SavedViewList, StringComparison.Ordinal) &&
                                                    !(p.Contains("filter=", StringComparison.Ordinal) && p.Contains("sort=", StringComparison.Ordinal)));
        Assert.Contains(result.Problems, p => p.StartsWith($"{PrintPlantModule.PairedReport} {holder}", StringComparison.Ordinal) &&
                                              p.Contains("detailed=true", StringComparison.Ordinal) && p.Contains("withNotes=true", StringComparison.Ordinal) &&
                                              p.Contains("which no other endpoint those permissions open shows", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => p.StartsWith(PrintPlantModule.PairedReport, StringComparison.Ordinal) &&
                                                    !(p.Contains("detailed=true", StringComparison.Ordinal) && p.Contains("withNotes=true", StringComparison.Ordinal)));
        Assert.True(result.Combinations > 0 && result.CombinationRuns > 0, $"{result.Combinations} combinations, {result.CombinationRuns} runs with them");
    }
}
