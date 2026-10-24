using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>Its own environment: the run creates and changes records with every endpoint that
/// changes data (the workspace's own settings among them), and reads the whole audit trail as the
/// record of what each request wrote, so nothing else may write in it at the same time.</summary>
public sealed class WriteSetFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync();

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G2: every action is denied unless a role grants it, inside an endpoint as well as at its door.
/// A request authorised by a permission writes only what that permission's reviewed write set
/// covers (<see cref="WriteSets"/>).
/// </summary>
public sealed class G2WriteSetTests(WriteSetFixture fixture) : IClassFixture<WriteSetFixture>
{
    [Fact]
    public async Task Each_permission_writes_only_its_reviewed_tables_and_columns()
    {
        var result = await WriteSets.RunAsync(fixture.Env);
        var output = TestContext.Current.TestOutputHelper;
        output?.WriteLine($"{result.Exercised.Count} endpoints exercised, {result.Requests} requests, {result.Writes} audited row changes judged");
        foreach (var line in result.Exercised.Concat(result.Unexercised.Select(u => $"{u} [reviewed as unreachable]")))
        {
            output?.WriteLine(line);
        }
        Assert.True(result.Problems.Count == 0, string.Join("\n", result.Problems));
        Assert.True(result.Exercised.Count >= Ratchet.Min("g2.writeSetEndpointsExercised"),
            $"g2.writeSetEndpointsExercised: {result.Exercised.Count}; ratchet minimum {Ratchet.Min("g2.writeSetEndpointsExercised")}");
        Assert.True(result.Writes >= Ratchet.Min("g2.writeSetWritesJudged"),
            $"g2.writeSetWritesJudged: {result.Writes}; ratchet minimum {Ratchet.Min("g2.writeSetWritesJudged")}");
        Assert.True(result.Observed.Count >= Ratchet.Min("g2.writeSetLines"),
            $"g2.writeSetLines: {result.Observed.Count}; ratchet minimum {Ratchet.Min("g2.writeSetLines")}");
        // Critic p02 round 14, plant PB3: requests are also sent at records with dependents, and the
        // edits that matter most reach them.
        output?.WriteLine($"{result.DependentsMade} dependent rows made; at records with dependents: {string.Join(", ", result.ExercisedWithDependents ?? [])}");
        foreach (var key in new[] { "PUT /api/tenancy/companies/{id:guid}", "PUT /api/tenancy/branches/{id:guid}", "PUT /api/identity/users/{id:guid}",
                     "DELETE /api/identity/users/{id:guid}", "PUT /api/identity/roles/{id:guid}", "DELETE /api/identity/roles/{id:guid}", "PUT /api/tenancy/access/{userId:guid}" })
        {
            Assert.Contains(key, result.ExercisedWithDependents ?? []);
        }
        Assert.True((result.ExercisedWithDependents?.Count ?? 0) >= Ratchet.Min("g2.writeSetEndpointsWithDependents"),
            $"g2.writeSetEndpointsWithDependents: {result.ExercisedWithDependents?.Count}; ratchet minimum {Ratchet.Min("g2.writeSetEndpointsWithDependents")}");
        Assert.True(result.DependentsMade >= Ratchet.Min("g2.writeSetDependentsMade"),
            $"g2.writeSetDependentsMade: {result.DependentsMade}; ratchet minimum {Ratchet.Min("g2.writeSetDependentsMade")}");
    }

    /// <summary>The write sets are what the owner's bar says: the workplace switch writes the
    /// caller's own starting place and nothing of any company; only a company permission writes a
    /// company.</summary>
    [Fact]
    public void The_reviewed_write_sets_keep_company_settings_to_company_permissions()
    {
        var lines = WriteSets.Load();
        Assert.Contains(lines, l => l.Permission == "tenancy.workplace.switch" && l.Table == "tenancy.user_workplaces");
        Assert.DoesNotContain(lines, l => l.Permission == "tenancy.workplace.switch" && l.Table != "tenancy.user_workplaces");
        Assert.All(lines.Where(l => l.Table == "tenancy.companies"), l => Assert.StartsWith("tenancy.companies.", l.Permission, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Permission == "*" && l.Table.StartsWith("tenancy.", StringComparison.Ordinal));
    }

    /// <summary>Plant P1b of critic p02 round 10: a switch declaring tenancy.workplace.switch that
    /// also sets the company's Arabic-legal-name requirement passed every gate. The write-set check
    /// must name it, and judge it from a caller holding that permission alone.</summary>
    [Fact]
    public async Task The_write_set_check_catches_a_switch_that_changes_a_company_setting()
    {
        await using var planted = fixture.Env.OverHost(WriteSetPlantModule.Settings(fixture.Env));
        var result = await WriteSets.RunAsync(planted, e => e.Pattern == WriteSetPlantModule.Route);
        foreach (var problem in result.Problems.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        Assert.Contains(result.Problems, p => p.StartsWith($"PUT {WriteSetPlantModule.Route} [", StringComparison.Ordinal) &&
                                              p.Contains("as a user holding exactly [tenancy.workplace.switch]", StringComparison.Ordinal) &&
                                              p.Contains("wrote update tenancy.companies (arabic_legal_name_required)", StringComparison.Ordinal) &&
                                              p.Contains("outside the write set of tenancy.workplace.switch", StringComparison.Ordinal));
        // Plant P1d of critic p02 round 11: a write of a value that may equal the stored one (the
        // fiscal year start month, 1 to 12) is seen through the request sending another valid value.
        Assert.Contains(result.Problems, p => p.StartsWith($"PUT {WriteSetPlantModule.Route} [fiscalYearStartMonth=12]", StringComparison.Ordinal) &&
                                              p.Contains("wrote update tenancy.companies (fiscal_year_start_month)", StringComparison.Ordinal) &&
                                              p.Contains("outside the write set of tenancy.workplace.switch", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains(WriteSetPlantModule.Route, StringComparison.Ordinal));
    }

    /// <summary>Plant PB3 of critic p02 round 14: closing a company also closed every branch of it,
    /// for a caller holding tenancy.companies.update but not tenancy.branches.update, and all 187
    /// gates passed, because every record the write-set check edited was fresh and a fresh company has
    /// no branches. The check now also sends each request at a record that has dependents (a company
    /// with a branch, a user of it and someone working there) and must name the branch write, from a
    /// caller holding tenancy.companies.update alone.</summary>
    [Fact]
    public async Task The_write_set_check_catches_closing_a_company_that_also_closes_its_branches()
    {
        await using var planted = fixture.Env.OverHost(WriteSetPlantModule.Settings(fixture.Env));
        var result = await WriteSets.RunAsync(planted, e => e.Pattern == WriteSetPlantModule.CompanyRoute);
        foreach (var problem in result.Problems.Take(10))
        {
            TestContext.Current.TestOutputHelper?.WriteLine(problem);
        }
        Assert.Contains(result.Problems, p => p.StartsWith($"PUT {WriteSetPlantModule.CompanyRoute} [isActive turned{WriteSets.OnDependents}]", StringComparison.Ordinal) &&
                                              p.Contains("as a user holding exactly [tenancy.companies.update]", StringComparison.Ordinal) &&
                                              p.Contains("wrote update tenancy.branches (", StringComparison.Ordinal) &&
                                              p.Contains("outside the write set of tenancy.companies.update", StringComparison.Ordinal));
        // The same request at a fresh company writes nothing outside the set: the dependents are
        // what shows the cascade.
        Assert.DoesNotContain(result.Problems, p => p.StartsWith($"PUT {WriteSetPlantModule.CompanyRoute} [isActive turned]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Problems, p => !p.Contains(WriteSetPlantModule.CompanyRoute, StringComparison.Ordinal));
        Assert.True(result.DependentsMade >= 4, $"dependents made: {result.DependentsMade}");
    }

    /// <summary>Collection names and the names of their records (a route parameter <c>companyId</c>
    /// finds <c>/api/tenancy/companies</c>, a field <c>branchId</c> names a branch).</summary>
    [Fact]
    public void Collections_and_their_record_names_match()
    {
        Assert.Equal("company", WriteSetDependents.Stem("/api/tenancy/companies"));
        Assert.Equal("branch", WriteSetDependents.Stem("/api/tenancy/branches"));
        Assert.Equal("user", WriteSetDependents.Stem("/api/identity/users"));
        Assert.Equal("role", WriteSetDependents.Stem("/api/identity/roles"));
        Assert.Equal("companies", WriteSetDependents.Plural("company"));
        Assert.Equal("branches", WriteSetDependents.Plural("branch"));
        Assert.Equal("users", WriteSetDependents.Plural("user"));
        Assert.Equal("keys", WriteSetDependents.Plural("key"));
    }

    /// <summary>The judgement itself: a write is covered only by a line of its own permission (or
    /// <c>*</c>), for its table, its action and every column it changed.</summary>
    [Fact]
    public void A_write_is_covered_only_by_its_permissions_table_action_and_columns()
    {
        var lines = new List<WriteSets.Line>
        {
            new("tenancy.workplace.switch", "tenancy.user_workplaces", new HashSet<string> { "insert", "update" }, null, "own row"),
            new("tenancy.companies.update", "tenancy.companies", new HashSet<string> { "update" }, new HashSet<string> { "legal_name_en" }, "names"),
        };
        var write = (string table, string action, params string[] columns) => new WriteSets.Write(1, table, action, columns);
        Assert.Empty(WriteSets.Outside("tenancy.workplace.switch", [write("tenancy.user_workplaces", "update", "company_id")], lines));
        Assert.Single(WriteSets.Outside("tenancy.workplace.switch", [write("tenancy.user_workplaces", "delete", "company_id")], lines));
        Assert.Single(WriteSets.Outside("tenancy.workplace.switch", [write("tenancy.companies", "update", "arabic_legal_name_required")], lines));
        Assert.Empty(WriteSets.Outside("tenancy.companies.update", [write("tenancy.companies", "update", "legal_name_en")], lines));
        Assert.Single(WriteSets.Outside("tenancy.companies.update", [write("tenancy.companies", "update", "legal_name_en", "arabic_legal_name_required")], lines));
        Assert.NotEmpty(WriteSets.FileProblems([new("tenancy.nothing", "tenancy.companies", new HashSet<string> { "update" }, null, "x")], ["tenancy.companies.update"]));
    }
}
