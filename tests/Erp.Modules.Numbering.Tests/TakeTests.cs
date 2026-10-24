using System.Diagnostics;
using Erp.Modules.Numbering.Contracts;
using Erp.Testing;

namespace Erp.Modules.Numbering.Tests;

/// <summary>
/// The contract later modules take numbers through (<see cref="IDocumentNumbers"/>), against
/// PostgreSQL itself: many documents at once on a gapless sequence and on a standard one, documents
/// abandoned half way, periods and branches, a pattern changed so it would repeat a number, and units
/// of work that try to number another tenant's or another company's documents. Each test uses a
/// document type and a year of its own, so the tests share the workspace without sharing counters.
/// </summary>
public sealed class TakeTests(NumberingFixture fixture) : IClassFixture<NumberingFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private async Task<Guid> CompanyAsync(int index = 0) => (await Db.CompaniesAsync(Env, Env.TenantA.Id))[index];

    private async Task<long> IssuedAsync(Guid company, string type, string number) =>
        await Db.ScalarAsync(Env, "SELECT count(*) FROM numbering.issued_numbers WHERE company_id = @c AND document_type = @t AND number = @n",
            ("c", company), ("t", type), ("n", number));

    [Fact]
    public async Task Gapless_numbers_taken_by_many_documents_at_once_run_on_with_no_gap_or_repeat_though_some_documents_are_abandoned()
    {
        var company = await CompanyAsync();
        var date = new DateOnly(2031, 5, 1);
        const int documents = 24;
        var committed = new System.Collections.Concurrent.ConcurrentBag<long>();
        var abandoned = 0;
        // Every document holds its number a little while before it is saved or abandoned, so the
        // others queue on the counter.
        await Task.WhenAll(Enumerable.Range(0, documents).Select(i => Task.Run(async () =>
        {
            await using var unit = await Unit.JobAsync(Env, Env.TenantA.Id);
            var issued = await unit.TakeAsync("sales.invoice", company, date);
            Assert.True(issued.Gapless);
            await Task.Delay(15);
            if (i % 3 == 0)
            {
                await unit.RollbackAsync();
                Interlocked.Increment(ref abandoned);
            }
            else
            {
                await unit.CommitAsync();
                committed.Add(issued.Value);
            }
        })));
        Assert.Equal(documents / 3, abandoned);
        // The saved documents hold exactly 1, 2, 3, … with no gap and no repeat.
        Assert.Equal(Enumerable.Range(1, documents - abandoned).Select(v => (long)v), committed.Order());
        Assert.Equal(documents - abandoned, await Db.ScalarAsync(Env,
            "SELECT count(*) FROM numbering.issued_numbers WHERE company_id = @c AND document_type = 'sales.invoice' AND period = '2031'", ("c", company)));
        Assert.Equal(documents - abandoned + 1, await Db.ScalarAsync(Env,
            "SELECT next_value FROM numbering.counters c JOIN numbering.sequences s ON s.id = c.sequence_id WHERE s.company_id = @c AND s.document_type = 'sales.invoice' AND c.period = '2031'", ("c", company)));
        Assert.Equal(1, await IssuedAsync(company, "sales.invoice", $"INV-2031-{documents - abandoned:00000}"));
    }

    [Fact]
    public async Task A_gapless_take_waits_for_the_open_document_then_gets_its_number_back_when_that_document_is_abandoned()
    {
        var company = await CompanyAsync();
        var date = new DateOnly(2032, 1, 10);
        await using var first = await Unit.JobAsync(Env, Env.TenantA.Id);
        var held = await first.TakeAsync("sales.creditNote", company, date);
        Assert.Equal(("CN-2032-00001", 1L, "2032"), (held.Number, held.Value, held.Period));

        await using var second = await Unit.JobAsync(Env, Env.TenantA.Id);
        var waiting = second.TakeAsync("sales.creditNote", company, date);
        await Task.Delay(700);
        Assert.False(waiting.IsCompleted, "a second document of a gapless sequence must wait while the first holds the next number");
        await first.RollbackAsync();
        var after = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("CN-2032-00001", after.Number);
        await second.CommitAsync();
        Assert.Equal(1, await IssuedAsync(company, "sales.creditNote", "CN-2032-00001"));
    }

    [Fact]
    public async Task A_standard_take_never_waits_for_an_open_document_and_a_document_that_fails_leaves_a_gap()
    {
        var company = await CompanyAsync();
        var date = new DateOnly(2033, 3, 3);
        await using var open = await Unit.JobAsync(Env, Env.TenantA.Id);
        var first = await open.TakeAsync("purchasing.bill", company, date);
        Assert.False(first.Gapless);
        Assert.Equal("BILL-2033-00001", first.Number);

        await using (var other = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            var clock = Stopwatch.StartNew();
            var second = await other.TakeAsync("purchasing.bill", company, date).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"a standard take waited {clock.Elapsed} for an open document");
            Assert.Equal("BILL-2033-00002", second.Number);
            await other.CommitAsync();
        }
        // The first document is abandoned: its number stays given (recorded), and is never reused.
        await open.RollbackAsync();
        await using var third = await Unit.JobAsync(Env, Env.TenantA.Id);
        Assert.Equal("BILL-2033-00003", (await third.TakeAsync("purchasing.bill", company, date)).Number);
        await third.CommitAsync();
        Assert.Equal(1, await IssuedAsync(company, "purchasing.bill", "BILL-2033-00001"));

        // Many at once: every number once, and nobody waits for anybody.
        var numbers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            await using var unit = await Unit.JobAsync(Env, Env.TenantA.Id);
            var issued = await unit.TakeAsync("purchasing.bill", company, date);
            await unit.CommitAsync();
            return issued.Value;
        })));
        Assert.Equal(Enumerable.Range(4, 20).Select(v => (long)v), numbers.Order());
    }

    [Fact]
    public async Task Each_period_and_each_branch_counts_on_its_own_and_the_preview_is_the_number_the_next_take_gets()
    {
        var company = await CompanyAsync(1);
        var branches = await Db.BranchesAsync(Env, company);
        await Db.SetAsync(Env, company, "inventory.receipt", "GRN-{BRANCH}-{YY}{MM}-", NumberReset.Monthly, gapless: false, perBranch: true, padding: 3);
        await using var unit = await Unit.JobAsync(Env, Env.TenantA.Id);
        var january = new DateOnly(2034, 1, 31);
        var preview = await unit.Numbers.PreviewAsync(new NumberRequest("inventory.receipt", company, january, branches[0]), CancellationToken.None);
        var taken = await unit.TakeAsync("inventory.receipt", company, january, branches[0]);
        Assert.Equal(preview.Number, taken.Number);
        Assert.EndsWith("-3401-001", taken.Number, StringComparison.Ordinal);
        Assert.Equal("2034-01", taken.Period);
        Assert.Equal(1, (await unit.TakeAsync("inventory.receipt", company, january.AddDays(1), branches[0])).Value);
        Assert.Equal(1, (await unit.TakeAsync("inventory.receipt", company, january, branches[1])).Value);
        Assert.Equal(2, (await unit.TakeAsync("inventory.receipt", company, january, branches[0])).Value);
        Assert.Equal(3, (await unit.Numbers.PreviewAsync(new NumberRequest("inventory.receipt", company, january, branches[0]), CancellationToken.None)).Value);

        // A sequence counted per branch needs the branch; a branch of another company is refused.
        var missing = await Assert.ThrowsAsync<NumberingException>(() => unit.TakeAsync("inventory.receipt", company, january));
        Assert.Equal(NumberingException.BranchRequired, missing.Code);
        var otherCompany = await CompanyAsync(0);
        var foreign = (await Db.BranchesAsync(Env, otherCompany))[0];
        Assert.Equal(NumberingException.BranchNotOfCompany, (await Assert.ThrowsAsync<NumberingException>(() => unit.TakeAsync("inventory.receipt", company, january, foreign))).Code);
        await unit.CommitAsync();
    }

    [Fact]
    public async Task A_pattern_changed_so_that_it_would_repeat_a_given_number_is_refused_and_takes_nothing()
    {
        var company = await CompanyAsync(1);
        await Db.SetAsync(Env, company, "payments.receipt", "R{YY}-", NumberReset.Yearly, gapless: false);
        var date = new DateOnly(2035, 6, 1);
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            Assert.Equal("R35-00001", (await unit.TakeAsync("payments.receipt", company, date)).Number);
            await unit.CommitAsync();
        }
        // The same text from a new counter (the sequence no longer starts again each year).
        await Db.SetAsync(Env, company, "payments.receipt", "R{YY}-", NumberReset.Never, gapless: false);
        var counters = await Db.ScalarAsync(Env, "SELECT count(*) FROM numbering.counters c JOIN numbering.sequences s ON s.id = c.sequence_id WHERE s.company_id = @c AND s.document_type = 'payments.receipt'", ("c", company));
        await using (var unit = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            var refused = await Assert.ThrowsAsync<NumberingException>(() => unit.TakeAsync("payments.receipt", company, date));
            Assert.Equal((NumberingException.NumberTaken, "R35-00001"), (refused.Code, refused.Arguments[0]));
        }
        Assert.Equal(counters, await Db.ScalarAsync(Env, "SELECT count(*) FROM numbering.counters c JOIN numbering.sequences s ON s.id = c.sequence_id WHERE s.company_id = @c AND s.document_type = 'payments.receipt'", ("c", company)));
        Assert.Equal(1, await IssuedAsync(company, "payments.receipt", "R35-00001"));
    }

    [Fact]
    public async Task A_unit_of_work_numbers_only_its_own_tenants_and_companies_documents_and_leaves_the_others_untouched()
    {
        var mine = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var theirs = await Db.CompaniesAsync(Env, Env.TenantB.Id);
        const string state = "SELECT coalesce(string_agg(id::text || ':' || next_value || ':' || coalesce(last_issued, 0), ',' ORDER BY id), '') FROM numbering.counters WHERE tenant_id = @t";
        var before = await Db.TextAsync(Env, state, ("t", Env.TenantB.Id));
        var date = new DateOnly(2036, 2, 2);

        // System work of tenant A sees every company of tenant A and nothing of tenant B.
        await using (var job = await Unit.JobAsync(Env, Env.TenantA.Id))
        {
            foreach (var company in theirs)
            {
                var refused = await Assert.ThrowsAsync<NumberingException>(() => job.TakeAsync("purchasing.order", company, date));
                Assert.Equal(NumberingException.SequenceMissing, refused.Code);
                await Assert.ThrowsAsync<NumberingException>(() => job.Numbers.PreviewAsync(new NumberRequest("purchasing.order", company, date), CancellationToken.None));
            }
            await job.CommitAsync();
        }
        // A user of tenant A who works in the first company only: neither the other company nor tenant B's.
        var admin = await Db.UserAsync(Env, Env.Email(Env.TenantA, "admin"));
        await using (var user = await Unit.UserAsync(Env, Env.TenantA.Id, admin, mine[0]))
        {
            foreach (var company in theirs.Append(mine[1]))
            {
                var refused = await Assert.ThrowsAsync<NumberingException>(() => user.TakeAsync("purchasing.order", company, date));
                Assert.Equal(NumberingException.CompanyNotAllowed, refused.Code);
            }
            var own = await user.TakeAsync("purchasing.order", mine[0], date);
            Assert.Equal("PO-2036-00001", own.Number);
            await user.CommitAsync();
        }
        Assert.Equal(before, await Db.TextAsync(Env, state, ("t", Env.TenantB.Id)));
        Assert.Equal(0, await Db.ScalarAsync(Env, "SELECT count(*) FROM numbering.issued_numbers WHERE tenant_id = @t AND period = '2036'", ("t", Env.TenantB.Id)));
    }

    [Fact]
    public async Task Every_number_given_is_in_the_audit_trail_with_who_took_it_and_traces_to_its_document()
    {
        var mine = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var admin = await Db.UserAsync(Env, Env.Email(Env.TenantA, "admin"));
        var document = Guid.NewGuid();
        await using (var user = await Unit.UserAsync(Env, Env.TenantA.Id, admin, mine[0]))
        {
            var issued = await user.Numbers.TakeAsync(new NumberRequest("sales.quotation", mine[0], new DateOnly(2037, 4, 4), null, document), CancellationToken.None);
            Assert.Equal("QT-2037-00001", issued.Number);
            await user.CommitAsync();
        }
        Assert.Equal(document.ToString(), await Db.TextAsync(Env, "SELECT source_id FROM numbering.issued_numbers WHERE number = 'QT-2037-00001' AND company_id = @c", ("c", mine[0])));
        // The standard sequence's own short transaction records the same actor as the document's.
        Assert.Equal(1, await Db.ScalarAsync(Env,
            "SELECT count(*) FROM audit.entries e JOIN numbering.issued_numbers i ON i.id = e.record_id WHERE e.table_name = 'issued_numbers' AND e.actor_id = @u AND e.actor_kind = 'user' AND i.number = 'QT-2037-00001'",
            ("u", admin)));
        Assert.True(await Db.ScalarAsync(Env,
            "SELECT count(*) FROM audit.entries WHERE table_schema = 'numbering' AND table_name = 'counters' AND actor_id = @u AND changes::text LIKE '%2037%'", ("u", admin)) >= 1);
    }

    [Fact]
    public async Task Unknown_document_types_and_dates_beyond_the_range_are_refused()
    {
        var company = await CompanyAsync();
        await using var unit = await Unit.JobAsync(Env, Env.TenantA.Id);
        Assert.Equal(NumberingException.DocumentTypeUnknown, (await Assert.ThrowsAsync<NumberingException>(() => unit.TakeAsync("sales.unknown", company, new DateOnly(2030, 1, 1)))).Code);
        Assert.Equal(NumberingException.DateOutOfRange, (await Assert.ThrowsAsync<NumberingException>(() => unit.TakeAsync("sales.order", company, new DateOnly(1989, 12, 31)))).Code);
    }
}
