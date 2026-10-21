using Erp.Gates.Tests.Infrastructure;
using Erp.Gates.Tests.SelfTests;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// The list gates' large-list profile: both tenants hold a demo volume of users (tenant B a little
/// more, so every whole-list answer differs between them), so the lists over users (the users list
/// and the access list) are judged where code for big lists runs. The planner statistics are fresh
/// (ANALYZE), as on a database that has been running a while: an estimate taken from them is close to
/// the whole table's row count, never to one tenant's.
/// </summary>
public sealed class G1VolumeFixture : IAsyncLifetime
{
    public const int VolumeA = SeedPlan.DemoVolume;
    public const int VolumeB = SeedPlan.DemoVolume + SeedPlan.DemoVolume / 40;

    public ErpTestEnvironment Env { get; private set; } = null!;

    /// <summary>
    /// A fresh canary of "CNRY" and twelve decimal digits. The usual canary's hexadecimal digits
    /// bring the letters a to f into every text value of tenant B, so which of B's rows hold an 'a'
    /// (a text filter and a search of <see cref="ListVolume"/>) changed from run to run, and the
    /// gate's tallies with it. The letters c, n, r and y are none of the letters the check filters
    /// or searches by (<see cref="ListVolume.CheckedLetters"/>), so every count is the same on every run.
    /// </summary>
    public static string NewCanary() =>
        "CNRY" + System.Security.Cryptography.RandomNumberGenerator.GetString("0123456789", 12);

    public async ValueTask InitializeAsync()
    {
        SqlTrace.EnsureStarted();
        Env = await ErpTestEnvironment.StartAsync(SeedPlan.GateAtVolume(NewCanary(), ErpTestEnvironment.Password, VolumeA, VolumeB));
        await using var admin = await Env.OpenAdminAsync();
        await DbCatalog.ExecuteAsync(admin, "ANALYZE");
    }

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// G1 for list answers at demo volume (critic p05 round 9, plant L12: past 10,000 rows the users
/// list's total became pg_class.reltuples, every tenant's users, and all 56 G1 tests passed because
/// every gate environment holds 25 users per tenant). See <see cref="ListVolume"/>. Also, no request
/// may read figures that describe whole tables (statistics, pg_class estimates, EXPLAIN): the SQL
/// trace sees every statement the requests send.
/// </summary>
public sealed class G1ListVolumeTests(G1VolumeFixture fixture) : IClassFixture<G1VolumeFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task Every_list_answers_each_tenant_from_its_own_rows_at_demo_volume()
    {
        using var a = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        using var b = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        var result = await ListVolume.RunAsync(Env, a, b, G1VolumeFixture.VolumeA);
        foreach (var line in result.Summary)
        {
            TestContext.Current.TestOutputHelper?.WriteLine(line);
        }
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{result.Queries} queries ({result.Discriminating} with different true answers), {result.Answers} answers judged, {result.RowsWalked} rows walked, " +
            $"{result.ListsAtVolume} lists at volume, {result.DeepAnswers} deep answers, {result.UnrankedSearches} unranked and {result.RankedSearches} ranked searches, {result.CursorsFollowed} cursors followed, " +
            $"{result.SearchBranches} search branches judged");

        var failures = new List<string>();
        void CheckAtLeast(int value, string key)
        {
            if (value < Ratchet.Min(key))
            {
                failures.Add($"{key}: {value}; ratchet minimum {Ratchet.Min(key)}");
            }
        }
        if (result.Wrong.Count > 0)
        {
            failures.Add($"{result.Wrong.Count} list answers at volume that are not the asking tenant's own:\n" + string.Join("\n", result.Wrong.Take(40)));
        }
        if (result.Blind.Count > 0)
        {
            failures.Add("The list check at volume may be blind:\n" + string.Join("\n", result.Blind));
        }
        var reads = SqlTrace.WholeTableReadsFor(Env);
        if (reads.Count > 0)
        {
            failures.Add("Requests read figures that describe whole tables (every tenant's rows):\n" + string.Join("\n", reads.Take(20)));
        }
        CheckAtLeast(result.ListsAtVolume, "g1.listsAtVolume");
        CheckAtLeast(result.Queries, "g1.listVolumeQueries");
        CheckAtLeast(result.Answers, "g1.listVolumeAnswers");
        CheckAtLeast(result.Discriminating, "g1.listVolumeDiscriminating");
        CheckAtLeast(result.DeepAnswers, "g1.listVolumeDeepAnswers");
        CheckAtLeast(result.UnrankedSearches, "g1.listVolumeUnrankedSearches");
        CheckAtLeast(result.RankedSearches, "g1.listVolumeRankedSearches");
        CheckAtLeast(result.CursorsFollowed, "g1.listVolumeCursors");
        CheckAtLeast(result.SearchBranches, "g1.listVolumeSearchBranches");
        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
    }

    /// <summary>
    /// Self-test: on a host over the same database where the users list's total past 10,000 rows is
    /// taken from whole-table figures or kept for the next tenant (<see cref="VolumePlantModule"/>:
    /// L12's reltuples, a total memo for big filtered queries, an EXPLAIN estimate for one-letter
    /// searches), and where every other search's total, broad or ranked, is kept for the next tenant
    /// (L13), the volume check reports each (each search branch on its own), and the trace reports
    /// the catalog reads and the EXPLAIN.
    /// </summary>
    [Fact]
    public async Task The_volume_check_catches_totals_taken_from_whole_table_figures_or_kept_for_big_lists()
    {
        await using var planted = Env.OverHost(VolumePlantModule.Settings(Env));
        using var a = await planted.SignInAsync(planted.Email(planted.TenantA, "admin"));
        using var b = await planted.SignInAsync(planted.Email(planted.TenantB, "admin"));
        var result = await ListVolume.RunAsync(planted, a, b, G1VolumeFixture.VolumeA, l => l.Key == "identity.users");
        var wrong = string.Join("\n", result.Wrong.Take(60));
        bool Found(Func<string, bool> uri) => result.Wrong.Any(w => w.Contains("answered total", StringComparison.Ordinal) && uri(w.Split(' ', 4)[3]));
        // The request of a reported answer: "tenant B: GET <uri> answered total …".
        static string Search(string uri) =>
            Uri.TryCreate("http://gate" + uri.Split(' ')[0], UriKind.Absolute, out var parsed) ? System.Web.HttpUtility.ParseQueryString(parsed.Query)["search"] ?? "" : "";
        // V1 (L12): a whole-list query's total is every tenant's users.
        Assert.True(Found(u => !u.Contains("filter=", StringComparison.Ordinal) && !u.Contains("search=", StringComparison.Ordinal)), "V1 (reltuples) not caught:\n" + wrong);
        // V2: a big filtered query's total is the first asker's.
        Assert.True(Found(u => u.Contains("filter=", StringComparison.Ordinal) && !u.Contains("search=", StringComparison.Ordinal)), "V2 (total memo) not caught:\n" + wrong);
        // V3: a broad one-letter search's total is the planner's estimate.
        Assert.True(Found(u => Search(u).Trim().Length == 1), "V3 (EXPLAIN estimate) not caught:\n" + wrong);
        // V4 (L13): every other branch of the search engine, too broad to rank and ranked, keeps the
        // first asker's total; each required branch must be caught on its own, and a search with a filter too.
        var users = Env.Factory.Services.GetRequiredService<Erp.Kernel.Modules.ModuleCatalog>().ListBindings.Single(l => l.Definition.Key == "identity.users");
        var missed = new List<string>();
        foreach (var branch in ListVolume.BranchClasses(users, arabic: true, digits: true).Where(c => c.Required))
        {
            var caught = result.Wrong.Any(w => w.Contains("answered total", StringComparison.Ordinal) && w.Split(' ', 4)[3] is var uri &&
                ListVolume.SearchBranch(Search(uri), users.SearchesInitials) == branch.Shape &&
                int.Parse(w[(w.IndexOf("matching it are ", StringComparison.Ordinal) + "matching it are ".Length)..].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture) > Erp.Kernel.Lists.ListSearch.MaxRankedRows == branch.Broad);
            if (!caught)
            {
                missed.Add(branch.Name);
            }
        }
        Assert.True(missed.Count == 0, "V4 (L13: search totals kept for the next tenant) not caught for the branches: " + string.Join("; ", missed) + "\n" + wrong);
        Assert.True(Found(u => u.Contains("filter=", StringComparison.Ordinal) && Search(u).Trim().Length > 1), "V4 with a filter (a search's total kept with its filter) not caught:\n" + wrong);
        var reads = SqlTrace.WholeTableReadsFor(planted);
        Assert.Contains(reads, r => r.Contains("pg_class", StringComparison.Ordinal));
        Assert.Contains(reads, r => r.Contains("EXPLAIN", StringComparison.Ordinal));
    }
}

/// <summary>
/// The volume gate's tallies (ranked and unranked searches, discriminating queries, deep answers)
/// must be the same on every run, or a ratchet minimum set from one run fails another (integration
/// of p05 round 11). The canary is the run's only random input to tenant B's rows: it may hold none
/// of the letters the check searches or filters by, and tenant B's generated people do not depend on it.
/// </summary>
public sealed class G1ListVolumeDeterminismTests
{
    [Fact]
    public void The_volume_fixture_canary_holds_none_of_the_checked_letters_and_leaves_B_people_alone()
    {
        var canaries = Enumerable.Range(0, 200).Select(_ => G1VolumeFixture.NewCanary()).ToList();
        Assert.Equal(canaries.Count, canaries.Distinct(StringComparer.Ordinal).Count());
        foreach (var canary in canaries)
        {
            var plan = SeedPlan.GateAtVolume(canary, ErpTestEnvironment.Password, G1VolumeFixture.VolumeA, G1VolumeFixture.VolumeB);
            var b = plan.Tenants[1];
            // The canary marks B's names and its e-mail domain; the rest of those texts is fixed.
            var held = ListVolume.CheckedLetters.Where(l => canary.Contains(l, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(held.Count == 0, $"canary {canary} holds checked letters {string.Join(", ", held)}");
            Assert.Equal(canary, b.Canary);
            Assert.Equal(SeedPlan.GateVolumeSeedB, b.VolumeRowsSeed);
        }
    }
}
