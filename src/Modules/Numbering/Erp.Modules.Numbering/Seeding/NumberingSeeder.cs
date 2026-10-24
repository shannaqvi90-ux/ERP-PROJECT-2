using Erp.Kernel.Seeding;
using Erp.Modules.Numbering.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Numbering.Seeding;

/// <summary>
/// Gives every company of the workspace a sequence for each registered document type (any profile,
/// provisioned workspaces too; idempotent, so a document type registered later reaches every company
/// at the next seed run). The demo adds what a workspace in use for a while holds: counters part way
/// through the year, a few numbers already given, delivery notes counted per branch. The gate
/// workspaces differ from each other in their settings and counters (tenant B's prefixes and choices
/// are its own), so the list gates have answers that tell the two apart.
/// </summary>
internal sealed class NumberingSeeder(NumberingDbContext db, DefaultSequences defaults, ICompanyDirectory companies, ITenantDirectory tenants, IBranchDirectory branches) : ITenantSeeder
{
    public int Order => 120;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        var all = await companies.ListAsync(cancellationToken);
        var fresh = !await db.Sequences.AnyAsync(cancellationToken);
        await defaults.AddMissingAsync(all.Select(c => c.Id).ToList(), cancellationToken);
        if (!fresh || all.Count == 0)
        {
            return;
        }
        var today = await Sequences.SequenceEndpoints.TodayAsync(tenants, cancellationToken);
        switch (context.Plan.Profile)
        {
            case SeedProfile.Demo:
                await DemoAsync(all, today, cancellationToken);
                break;
            case SeedProfile.Gate:
                await GateAsync(context, all, today, cancellationToken);
                break;
        }
    }

    private async Task<NumberSequence> SequenceAsync(Guid company, string type, CancellationToken cancellationToken) =>
        await db.Sequences.SingleAsync(s => s.CompanyId == company && s.DocumentType == type, cancellationToken);

    /// <summary>A counter part way through its period, with its last few numbers recorded.</summary>
    private async Task CountedAsync(NumberSequence sequence, Guid? branch, string? branchCode, DateOnly today, long next, int recorded, CancellationToken cancellationToken)
    {
        var period = NumberPattern.PeriodOf(sequence.Reset, today);
        db.Counters.Add(new NumberCounter
        {
            CompanyId = sequence.CompanyId, SequenceId = sequence.Id, BranchId = branch, Period = period,
            NextValue = next, LastIssued = next - 1, LastIssuedAt = DateTimeOffset.UtcNow.AddHours(-2),
        });
        for (var value = Math.Max(1, next - recorded); value < next; value++)
        {
            var date = today.AddDays((int)(value - next + 1) / 3);
            if (sequence.Reset != NumberReset.Never && NumberPattern.PeriodOf(sequence.Reset, date) != period)
            {
                date = today;
            }
            db.Issued.Add(new IssuedDocumentNumber
            {
                CompanyId = sequence.CompanyId, SequenceId = sequence.Id, DocumentType = sequence.DocumentType, BranchId = branch, Period = period,
                Value = value, Number = NumberPattern.Format(sequence.Prefix, sequence.Suffix, sequence.Padding, date, branchCode, value),
                DocumentDate = date, Gapless = sequence.Gapless,
            });
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task DemoAsync(IReadOnlyList<CompanyInfo> all, DateOnly today, CancellationToken cancellationToken)
    {
        var first = all[0];
        await CountedAsync(await SequenceAsync(first.Id, "purchasing.order", cancellationToken), null, null, today, 148, 6, cancellationToken);
        await CountedAsync(await SequenceAsync(first.Id, "sales.invoice", cancellationToken), null, null, today, 1203, 8, cancellationToken);
        await CountedAsync(await SequenceAsync(first.Id, "sales.quotation", cancellationToken), null, null, today, 412, 4, cancellationToken);
        await CountedAsync(await SequenceAsync(first.Id, "accounting.journal", cancellationToken), null, null, today, 37, 3, cancellationToken);
        // Delivery notes leave each warehouse and showroom with that branch's own numbers.
        var receipt = await SequenceAsync(first.Id, "inventory.receipt", cancellationToken);
        receipt.Suffix = "/WH";
        var delivery = await SequenceAsync(first.Id, "inventory.delivery", cancellationToken);
        delivery.Prefix = "DN-{BRANCH}-{YY}-";
        delivery.Padding = 4;
        delivery.PerBranch = true;
        await db.SaveChangesAsync(cancellationToken);
        var next = 58L;
        foreach (var branch in await branches.ListAsync(first.Id, cancellationToken))
        {
            await CountedAsync(delivery, branch.Id, branch.Code, today, next, 2, cancellationToken);
            next += 31;
        }
        foreach (var company in all.Skip(1))
        {
            await CountedAsync(await SequenceAsync(company.Id, "sales.invoice", cancellationToken), null, null, today, 87 + company.Code.Length, 2, cancellationToken);
        }
    }

    private async Task GateAsync(TenantSeedContext context, IReadOnlyList<CompanyInfo> all, DateOnly today, CancellationToken cancellationToken)
    {
        var first = all[0];
        if (context.Tenant.Canary is null)
        {
            var payment = await SequenceAsync(first.Id, "payments.payment", cancellationToken);
            payment.Suffix = "/HQ";
            await db.SaveChangesAsync(cancellationToken);
            await CountedAsync(await SequenceAsync(first.Id, "purchasing.order", cancellationToken), null, null, today, 12, 2, cancellationToken);
            return;
        }
        // Tenant B numbers some documents its own way.
        var order = await SequenceAsync(first.Id, "purchasing.order", cancellationToken);
        order.Prefix = "BPO-{YYYY}-";
        order.Padding = 6;
        var quotation = await SequenceAsync(first.Id, "sales.quotation", cancellationToken);
        quotation.Prefix = "BQ-";
        quotation.Suffix = "/BR";
        quotation.Reset = NumberReset.Never;
        var delivery = await SequenceAsync(first.Id, "inventory.delivery", cancellationToken);
        delivery.Prefix = "BDN-{BRANCH}-";
        delivery.Reset = NumberReset.Never;
        delivery.PerBranch = true;
        await db.SaveChangesAsync(cancellationToken);
        await CountedAsync(order, null, null, today, 905, 2, cancellationToken);
        await CountedAsync(quotation, null, null, today, 31, 1, cancellationToken);
        var branch = (await branches.ListAsync(first.Id, cancellationToken)).FirstOrDefault();
        if (branch is not null)
        {
            await CountedAsync(delivery, branch.Id, branch.Code, today, 7, 1, cancellationToken);
        }
    }
}
