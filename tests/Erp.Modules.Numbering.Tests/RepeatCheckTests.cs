using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Kernel.Data;
using Erp.Modules.Numbering.Contracts;
using Erp.Modules.Numbering.Sequences;
using Erp.Modules.Tenancy.Contracts;
using Erp.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Numbering.Tests;

/// <summary>
/// Owner decision #32 rule 5 (docs/compliance/vat-tax-document-numbering.md): a pattern change that
/// could give a number already given is refused. The check costs the same statements however many
/// branches and periods there are, and it covers the periods numbers were already given in (a reset
/// change cannot give a number again once back-dated documents are taken).
/// </summary>
public sealed class RepeatCheckTests(NumberingFixture fixture) : IClassFixture<NumberingFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4));

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed class Counting : DbCommandInterceptor
    {
        public int Statements;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Statements);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class Branches(int count, Guid company) : IBranchDirectory
    {
        public Task<BranchInfo?> GetAsync(Guid branchId, CancellationToken cancellationToken) => Task.FromResult<BranchInfo?>(null);

        public Task<IReadOnlyList<BranchInfo>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BranchInfo>>(Enumerable.Range(0, count).Select(i => new BranchInfo(Guid.NewGuid(), company, $"B{i:D5}", "b", "ب", true)).ToList());
    }

    private async Task<(int Statements, string? Repeated)> CheckAsync(Guid company, int branches, string prefix, string reset, bool perBranch, int padding)
    {
        await using var unit = await Unit.JobAsync(Env, Env.TenantA.Id);
        var counting = new Counting();
        var options = new DbContextOptionsBuilder<NumberingDbContext>();
        DataRegistration.ConfigureNpgsql(options, unit.Session.Connection, NumberingDbContext.SchemaName);
        options.AddInterceptors(counting);
        await using var db = new NumberingDbContext(options.Options, unit.Session);
        var sequence = await db.Sequences.AsNoTracking().SingleAsync(s => s.CompanyId == company && s.DocumentType == "sales.invoice");
        var services = new ServiceCollection().AddSingleton<IBranchDirectory>(new Branches(branches, company)).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        counting.Statements = 0;
        var repeated = await SequenceEndpoints.RepeatedNumberAsync(db, http, sequence, prefix, "", padding, reset, perBranch, Today, CancellationToken.None);
        return (counting.Statements, repeated);
    }

    [Fact]
    public async Task The_repeat_check_reads_the_same_few_statements_with_three_branches_or_five_thousand()
    {
        var company = (await Db.CompaniesAsync(Env, Env.TenantA.Id))[1];
        await Db.SetAsync(Env, company, "sales.invoice", "INV-{BRANCH}-{YYYY}-", NumberReset.Yearly, gapless: true, perBranch: true);
        var branch = (await Db.BranchesAsync(Env, company))[0];
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            await unit.TakeAsync("sales.invoice", company, Today, branch);
            await unit.CommitAsync();
        }
        var few = await CheckAsync(company, 3, "INV-{BRANCH}-{YYYY}-", NumberReset.Yearly, true, 5);
        var many = await CheckAsync(company, 5000, "INV-{BRANCH}-{YYYY}-", NumberReset.Yearly, true, 5);
        Assert.Equal(few.Statements, many.Statements);
        Assert.InRange(many.Statements, 1, 4);
        Assert.Null(many.Repeated);
    }

    [Fact]
    public async Task Probe_backdated_month_a_yearly_to_monthly_change_that_would_give_the_months_numbers_again_is_refused_in_both_languages()
    {
        using var english = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var arabic = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var company = (await Db.CompaniesAsync(Env, Env.TenantA.Id))[0];
        await Db.SetAsync(Env, company, "sales.creditNote", "CN-{YYYY}-{MM}-", NumberReset.Yearly, gapless: true);
        var earlier = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            await unit.TakeAsync("sales.creditNote", company, earlier);
            await unit.TakeAsync("sales.creditNote", company, earlier);
            await unit.CommitAsync();
        }
        var rows = (await (await english.GetAsync("/api/numbering/sequences?take=200")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray();
        var row = rows.First(r => r.GetProperty("documentType").GetString() == "sales.creditNote" && r.GetProperty("companyId").GetGuid() == company);
        object Body(JsonElement r) => new
        {
            prefix = r.GetProperty("prefix").GetString(), suffix = r.GetProperty("suffix").GetString(), padding = r.GetProperty("padding").GetInt32(),
            reset = "monthly", gapless = true, perBranch = false, nextValue = (long?)null, version = r.GetProperty("version").GetUInt32(),
        };
        var refused = await english.PutAsJsonAsync($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}", Body(row));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var problem = (await Json(refused)).GetProperty("errors").GetProperty("prefix")[0];
        Assert.Equal("numberingPatternRepeatsNumber", problem.GetProperty("code").GetString());
        var inArabic = await arabic.PutAsJsonAsync($"/api/numbering/sequences/{row.GetProperty("id").GetGuid()}", Body(row));
        Assert.Equal(HttpStatusCode.BadRequest, inArabic.StatusCode);
        Assert.Matches(@"\p{IsArabic}", (await Json(inArabic)).GetProperty("errors").GetProperty("prefix")[0].GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task Probe_backdated_switch_never_to_yearly_is_refused_when_a_year_already_holds_numbers()
    {
        var company = (await Db.CompaniesAsync(Env, Env.TenantA.Id))[0];
        await Db.SetAsync(Env, company, "sales.invoice", "INV-{YYYY}-", NumberReset.Never, gapless: true);
        var date = new DateOnly(Today.Year - 1, 12, 20);
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            await unit.TakeAsync("sales.invoice", company, date);
            await unit.CommitAsync();
        }
        var result = await CheckAsync(company, 0, "INV-{YYYY}-", NumberReset.Yearly, false, 5);
        Assert.Equal($"INV-{date.Year}-00001", result.Repeated);
        // A pattern that prints something else cannot repeat it.
        Assert.Null((await CheckAsync(company, 0, "IN-{YYYY}-", NumberReset.Yearly, false, 5)).Repeated);
    }

    [Fact]
    public async Task Owner_rule_2_the_application_role_can_read_and_add_given_numbers_but_not_change_or_delete_them()
    {
        var company = (await Db.CompaniesAsync(Env, Env.TenantA.Id))[0];
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            await unit.TakeAsync("sales.invoice", company, Today);
            await unit.CommitAsync();
        }
        await using var app = await Env.OpenAppAsync();
        foreach (var statement in new[] { "UPDATE numbering.issued_numbers SET number = number || 'X'", "DELETE FROM numbering.issued_numbers" })
        {
            await using var command = new Npgsql.NpgsqlCommand(statement, app);
            var refused = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(Npgsql.PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
        }
    }
}
