using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Numbering.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.Numbering.Sequences;

/// <summary>A row of the document numbering list: how one company numbers one kind of document, and
/// the number its next document dated today would get (for a sequence counted per branch, in the
/// caller's working branch when it is one of the company's, else in the company's first branch).
/// <c>canUpdate</c> says whether the caller may change it: they hold the permission in that company
/// and work in every branch of it.</summary>
public sealed record SequenceRow(
    Guid Id,
    Guid CompanyId,
    string CompanyCode,
    string DocumentType,
    string NameEn,
    string NameAr,
    string Prefix,
    string Suffix,
    int Padding,
    string Reset,
    bool Gapless,
    bool PerBranch,
    string NextNumber,
    long NextValue,
    bool WorkingCompany,
    DateTimeOffset UpdatedAt,
    uint Version,
    bool CanUpdate);

/// <summary>A branch of the sequence's company, for previewing a number counted per branch.</summary>
public sealed record SequenceBranch(Guid Id, string Code, string NameEn, string NameAr);

/// <summary>One counter of the sequence: a period ("" when it never starts again, "2026", "2026-10")
/// and, when counted per branch, a branch.</summary>
public sealed record SequenceCounter(Guid? BranchId, string? BranchCode, string Period, long NextValue, long? LastIssued, DateTimeOffset? LastIssuedAt);

/// <summary>A number the sequence gave, latest first.</summary>
public sealed record SequenceIssued(string Number, DateOnly DocumentDate, DateTimeOffset IssuedAt, bool Gapless);

/// <summary>A sequence as its form shows it.</summary>
public sealed record SequenceDto(
    Guid Id,
    Guid CompanyId,
    string CompanyCode,
    string CompanyNameEn,
    string CompanyNameAr,
    string DocumentType,
    string NameEn,
    string NameAr,
    string Prefix,
    string Suffix,
    int Padding,
    string Reset,
    bool Gapless,
    bool PerBranch,
    [property: Description("The number the next document dated today would get (for a sequence counted per branch: in previewBranchId).")]
    string NextNumber,
    long NextValue,
    [property: Description("The branch the next number is previewed in (a sequence counted per branch), or null.")]
    Guid? PreviewBranchId,
    [property: Description("Today in the workspace's time zone: the date the next number is previewed for.")]
    DateOnly Today,
    IReadOnlyList<SequenceBranch> Branches,
    [property: Description("The sequence's counters, latest period first (at most 24).")]
    IReadOnlyList<SequenceCounter> Counters,
    [property: Description("The last numbers the sequence gave (at most 10).")]
    IReadOnlyList<SequenceIssued> Recent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    [property: Description("True when the caller may change this sequence: they hold numbering.sequences.update in its company and work in every branch of it.")]
    bool CanUpdate);

/// <summary>Change how a company numbers a kind of document. Prefix and suffix hold letters A to Z,
/// digits, the separators - / . _ # and the tokens {YYYY}, {YY}, {MM} and {BRANCH}; a sequence that
/// starts again every year needs a year token, one that starts again every month a year and {MM},
/// one counted per branch {BRANCH}. <c>nextValue</c> moves the counter of today's period (a sequence
/// the whole company counts): never below a number already given, and on a gapless sequence never
/// once a number of the period was given.</summary>
public sealed record UpdateSequenceRequest(
    [property: ApiExample("PO-{YY}{MM}-"), StringLength(NumberPattern.MaxAffixLength), RegularExpression(SequenceEndpoints.AffixPattern)] string? Prefix,
    [property: ApiExample("/DXB"), StringLength(NumberPattern.MaxAffixLength), RegularExpression(SequenceEndpoints.AffixPattern)] string? Suffix,
    [property: Range(NumberPattern.MinPadding, NumberPattern.MaxPadding)] int? Padding,
    [property: AllowedTextValues(NumberReset.Yearly, NumberReset.Monthly, NumberReset.Never)] string? Reset,
    bool? Gapless,
    bool? PerBranch,
    [property: Range(1L, NumberPattern.MaxValue), Description("The value the next number of today's period gets (a sequence the whole company counts): the record's own nextValue keeps it; leave it out, or send it unchanged, to keep counting on.")] long? NextValue,
    uint? Version);

internal static class SequenceEndpoints
{
    /// <summary>The characters a prefix or suffix may hold (tokens are checked by <see cref="NumberPattern.Check"/>).</summary>
    public const string AffixPattern = @"^[A-Za-z0-9\-/._#{}]*$";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/sequences", List)
            .WithName("numbering.sequences.list")
            .WithSummary("How each company numbers each kind of document, with the number the next document dated today would get, a page at a time: word search on the document type's names, company code, prefix, suffix and next number; filters, sort, paging and grouping (the list query contract); the working company's first.")
            .RequirePermission(NumberingPermissions.SequencesRead);

        group.MapGet("/sequences/{id:guid}", Get)
            .WithName("numbering.sequences.get")
            .WithSummary("One sequence: its pattern, the next number (previewed for today), its counters by period and branch, the last numbers it gave and the company's branches.")
            .RequirePermission(NumberingPermissions.SequencesRead);

        group.MapPut("/sequences/{id:guid}", Update)
            .WithName("numbering.sequences.update")
            .WithSummary("Change a sequence's prefix, digits, suffix, when it starts again, whether it may skip numbers, whether each branch counts on its own, and the next number of today's period (needs every branch of the company, which shares it).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(NumberingPermissions.SequencesUpdate);
    }

    private static async Task<Results<Ok<ListPage<SequenceRow>>, ProblemHttpResult>> List(
        NumberingDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<SequenceRow>> PageAsync(NumberingDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(db, http, null, cancellationToken);
        return await catalog.ListBinding<SequenceRow>(SequencesList.Key).QueryAsync(rows.AsQueryable(), request, http, cancellationToken);
    }

    /// <summary>Today in the workspace's time zone (documents are dated by the UAE calendar day).</summary>
    internal static async Task<DateOnly> TodayAsync(ITenantDirectory tenants, CancellationToken cancellationToken)
    {
        const string fallback = "Asia/Dubai";
        var tenant = await tenants.GetCurrentAsync(cancellationToken);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant?.TimeZone ?? fallback, out var found)
            ? found
            : TimeZoneInfo.TryFindSystemTimeZoneById(fallback, out var dubai) ? dubai : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    private sealed record Context(DateOnly Today, IReadOnlyDictionary<Guid, CompanyInfo> Companies, Guid? WorkingCompany, Guid? WorkingBranch);

    private static async Task<Context> ContextAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var companies = await services.GetRequiredService<ICompanyDirectory>().ListAsync(cancellationToken);
        var scope = services.GetRequiredService<ICompanyContext>();
        return new Context(await TodayAsync(services.GetRequiredService<ITenantDirectory>(), cancellationToken), companies.ToDictionary(c => c.Id),
            scope.ActiveCompanyId, scope.ActiveBranchId);
    }

    /// <summary>The branch a sequence counted per branch is previewed in: the working branch when it
    /// is one of the company's, else the company's first branch by code.</summary>
    private static SequenceBranch? PreviewBranch(Context context, Guid companyId, IReadOnlyList<SequenceBranch> branches) =>
        (context.WorkingCompany == companyId ? branches.FirstOrDefault(b => b.Id == context.WorkingBranch) : null) ?? branches.FirstOrDefault();

    private static async Task<List<SequenceRow>> RowsAsync(NumberingDbContext db, HttpContext http, Guid? id, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(http, cancellationToken);
        var types = http.RequestServices.GetRequiredService<DocumentTypeCatalog>();
        var sequences = db.Sequences.AsNoTracking();
        if (id is { } one)
        {
            sequences = sequences.Where(s => s.Id == one);
        }
        var found = await sequences.ToListAsync(cancellationToken);
        // Today's periods: the whole run, this year, this month.
        var year = context.Today.Year;
        var month = context.Today.Month;
        var counters = await db.Counters.AsNoTracking()
            .Where(c => ((c.PeriodYear == 0 && c.PeriodMonth == 0) || (c.PeriodYear == year && (c.PeriodMonth == 0 || c.PeriodMonth == month))) && (id == null || c.SequenceId == id))
            .Select(c => new { c.SequenceId, c.BranchId, c.PeriodYear, c.PeriodMonth, c.NextValue }).ToListAsync(cancellationToken);
        var next = counters.ToDictionary(c => (c.SequenceId, c.BranchId, c.PeriodYear, c.PeriodMonth), c => c.NextValue);
        var branches = new Dictionary<Guid, IReadOnlyList<SequenceBranch>>();
        foreach (var companyId in found.Where(s => s.PerBranch).Select(s => s.CompanyId).Distinct())
        {
            branches[companyId] = await BranchesAsync(http, companyId, cancellationToken);
        }
        var canUpdate = await UpdatableAsync(http, found.Select(s => s.CompanyId).Distinct(), cancellationToken);
        var rows = new List<SequenceRow>(found.Count);
        foreach (var s in found)
        {
            var company = context.Companies.GetValueOrDefault(s.CompanyId);
            var branch = s.PerBranch ? PreviewBranch(context, s.CompanyId, branches.GetValueOrDefault(s.CompanyId) ?? []) : null;
            var (periodYear, periodMonth) = NumberPattern.PeriodPartsOf(s.Reset, context.Today);
            var value = next.GetValueOrDefault((s.Id, s.PerBranch ? branch?.Id : null, periodYear, periodMonth), 1L);
            rows.Add(new SequenceRow(s.Id, s.CompanyId, company?.Code ?? "", s.DocumentType,
                types.Name(s.DocumentType, Languages.English), types.Name(s.DocumentType, Languages.Arabic),
                s.Prefix, s.Suffix, s.Padding, s.Reset, s.Gapless, s.PerBranch,
                NumberPattern.Format(s.Prefix, s.Suffix, s.Padding, context.Today, branch?.Code, value), value,
                s.CompanyId == context.WorkingCompany, s.UpdatedAt, s.Version, canUpdate.Contains(s.CompanyId)));
        }
        return rows;
    }

    private static async Task<IReadOnlyList<SequenceBranch>> BranchesAsync(HttpContext http, Guid companyId, CancellationToken cancellationToken) =>
        (await http.RequestServices.GetRequiredService<IBranchDirectory>().ListAsync(companyId, cancellationToken))
            .Where(b => b.IsActive).Select(b => new SequenceBranch(b.Id, b.Code, b.NameEn, b.NameAr)).ToList();

    /// <summary>The companies whose sequences the caller may change: they hold the update permission
    /// there and work in every branch of it.</summary>
    private static async Task<HashSet<Guid>> UpdatableAsync(HttpContext http, IEnumerable<Guid> companies, CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        var session = http.RequestServices.GetRequiredService<ICompanyContext>();
        var places = http.RequestServices.GetRequiredService<ICompanyPermissions>();
        var user = http.RequestServices.GetRequiredService<ICurrentUser>();
        if (!user.Permissions.Contains(NumberingPermissions.SequencesUpdate))
        {
            return result;
        }
        foreach (var company in companies)
        {
            if (session.HoldsEveryBranch(company) && await places.HoldsAsync(NumberingPermissions.SequencesUpdate, company, cancellationToken))
            {
                result.Add(company);
            }
        }
        return result;
    }

    private static async Task<Results<Ok<SequenceDto>, ProblemHttpResult>> Get(Guid id, NumberingDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var dto = await ToDtoAsync(db, http, id, cancellationToken);
        return dto is null ? Problems.NotFound(http) : TypedResults.Ok(dto);
    }

    private static async Task<Results<Ok<SequenceDto>, ProblemHttpResult>> Update(
        Guid id, UpdateSequenceRequest request, NumberingDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("padding", request.Padding)
            .Required("reset", request.Reset)
            .Required("gapless", request.Gapless)
            .Required("perBranch", request.PerBranch)
            .Required("version", request.Version);
        var prefix = request.Prefix?.Trim() ?? "";
        var suffix = request.Suffix?.Trim() ?? "";
        if (request.Padding is not null && request.Reset is not null && request.PerBranch is not null)
        {
            foreach (var problem in NumberPattern.Check(prefix, suffix, request.Padding, request.Reset, request.PerBranch.Value))
            {
                validator.Add(problem.Field, problem.Code, problem.Arguments);
            }
        }
        if (request.NextValue is { } typed)
        {
            validator.Must(typed is >= 1 and <= NumberPattern.MaxValue, "nextValue", "numberingNextNumberRange", NumberPattern.MaxValue);
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var sequence = await db.Sequences.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sequence is null)
        {
            return Problems.NotFound(http);
        }
        // A role given in another company grants nothing here, and every branch of the company shares
        // its sequences: the endpoint refuses first (the kernel refuses the write as well).
        if (!session.PermitsCompany(sequence.CompanyId))
        {
            return Problems.Forbidden(http, CompanyPermissionWriteException.Code);
        }
        if (!session.HoldsEveryBranch(sequence.CompanyId))
        {
            return Problems.Forbidden(http, "numbering.companyNeedsEveryBranch");
        }
        if (sequence.Version != request.Version)
        {
            return Problems.Conflict(http, "numbering.sequenceChanged");
        }
        var today = await TodayAsync(http.RequestServices.GetRequiredService<ITenantDirectory>(), cancellationToken);
        if (request.PerBranch!.Value || sequence.PerBranch)
        {
            // Each branch keeps its own next number: the record's own (the previewed branch's, as its
            // GET answers it) may come back unchanged, never moved for the whole company.
            if (request.NextValue is { } sent && sent != await PreviewValueAsync(db, http, sequence, today, cancellationToken))
            {
                return new Validator(http).Add("nextValue", "numberingNextNumberPerBranch").ToResult();
            }
        }
        else if (request.NextValue is not null || request.Reset != sequence.Reset)
        {
            // The counter of today's period. When the sequence starts again at another time from now
            // on, the new period's counter goes on from where today's ran (no number of today repeats).
            var (year, month) = NumberPattern.PeriodPartsOf(request.Reset!, today);
            var counter = await db.Counters.SingleOrDefaultAsync(c => c.SequenceId == id && c.BranchId == null && c.PeriodYear == year && c.PeriodMonth == month, cancellationToken);
            if (counter is null && request.Reset != sequence.Reset)
            {
                var (beforeYear, beforeMonth) = NumberPattern.PeriodPartsOf(sequence.Reset, today);
                if (await db.Counters.AsNoTracking().SingleOrDefaultAsync(c => c.SequenceId == id && c.BranchId == null && c.PeriodYear == beforeYear && c.PeriodMonth == beforeMonth, cancellationToken) is { } running)
                {
                    counter = new NumberCounter
                    {
                        CompanyId = sequence.CompanyId, SequenceId = sequence.Id, BranchId = null, PeriodYear = year, PeriodMonth = month,
                        NextValue = running.NextValue, LastIssued = running.LastIssued, LastIssuedAt = running.LastIssuedAt,
                    };
                    db.Counters.Add(counter);
                }
            }
            var current = counter?.NextValue ?? 1;
            if (request.NextValue is { } nextValue && nextValue != current)
            {
                if (counter?.LastIssued is { } last && nextValue <= last)
                {
                    return new Validator(http).Add("nextValue", "numberingNextNumberBelowIssued", last).ToResult();
                }
                if (request.Gapless == true && counter?.LastIssued is not null)
                {
                    return new Validator(http).Add("nextValue", "numberingGaplessNextNumberFixed", current).ToResult();
                }
                if (counter is null)
                {
                    db.Counters.Add(new NumberCounter { CompanyId = sequence.CompanyId, SequenceId = sequence.Id, BranchId = null, PeriodYear = year, PeriodMonth = month, NextValue = nextValue });
                }
                else
                {
                    counter.NextValue = nextValue;
                    if (db.Entry(counter).State != EntityState.Added)
                    {
                        db.Entry(counter).Property(c => c.UpdatedAt).IsModified = true;
                    }
                }
            }
        }
        db.Entry(sequence).Property(s => s.Version).OriginalValue = request.Version!.Value;
        sequence.Prefix = prefix;
        sequence.Suffix = suffix;
        sequence.Padding = request.Padding!.Value;
        sequence.Reset = request.Reset!;
        sequence.Gapless = request.Gapless!.Value;
        sequence.PerBranch = request.PerBranch!.Value;
        db.Entry(sequence).Property(s => s.UpdatedAt).IsModified = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problems.Conflict(http, "numbering.sequenceChanged");
        }
        return TypedResults.Ok((await ToDtoAsync(db, http, sequence.Id, cancellationToken))!);
    }

    /// <summary>The next value the record's GET answers: today's period's counter, in the previewed
    /// branch for a sequence counted per branch.</summary>
    private static async Task<long> PreviewValueAsync(NumberingDbContext db, HttpContext http, NumberSequence s, DateOnly today, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(http, cancellationToken);
        var branch = s.PerBranch ? PreviewBranch(context, s.CompanyId, await BranchesAsync(http, s.CompanyId, cancellationToken)) : null;
        var (year, month) = NumberPattern.PeriodPartsOf(s.Reset, today);
        var branchId = s.PerBranch ? branch?.Id : null;
        return await db.Counters.AsNoTracking().Where(c => c.SequenceId == s.Id && c.BranchId == branchId && c.PeriodYear == year && c.PeriodMonth == month)
            .Select(c => (long?)c.NextValue).SingleOrDefaultAsync(cancellationToken) ?? 1L;
    }

    private static async Task<SequenceDto?> ToDtoAsync(NumberingDbContext db, HttpContext http, Guid id, CancellationToken cancellationToken)
    {
        var s = await db.Sequences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (s is null)
        {
            return null;
        }
        var context = await ContextAsync(http, cancellationToken);
        var types = http.RequestServices.GetRequiredService<DocumentTypeCatalog>();
        var company = context.Companies.GetValueOrDefault(s.CompanyId);
        var branches = await BranchesAsync(http, s.CompanyId, cancellationToken);
        var codes = branches.ToDictionary(b => b.Id, b => b.Code);
        var counters = await db.Counters.AsNoTracking().Where(c => c.SequenceId == id)
            .OrderByDescending(c => c.PeriodYear).ThenByDescending(c => c.PeriodMonth).ThenBy(c => c.BranchId).Take(24)
            .ToListAsync(cancellationToken);
        var recent = await db.Issued.AsNoTracking().Where(i => i.SequenceId == id)
            .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).Take(10)
            .Select(i => new SequenceIssued(i.Number, i.DocumentDate, i.CreatedAt, i.Gapless))
            .ToListAsync(cancellationToken);
        var branch = s.PerBranch ? PreviewBranch(context, s.CompanyId, branches) : null;
        var (todayYear, todayMonth) = NumberPattern.PeriodPartsOf(s.Reset, context.Today);
        var value = counters.FirstOrDefault(c => c.PeriodYear == todayYear && c.PeriodMonth == todayMonth && c.BranchId == (s.PerBranch ? branch?.Id : null))?.NextValue ?? 1L;
        var canUpdate = (await UpdatableAsync(http, [s.CompanyId], cancellationToken)).Contains(s.CompanyId);
        return new SequenceDto(s.Id, s.CompanyId, company?.Code ?? "", company?.LegalNameEn ?? "", company?.LegalNameAr ?? "", s.DocumentType,
            types.Name(s.DocumentType, Languages.English), types.Name(s.DocumentType, Languages.Arabic),
            s.Prefix, s.Suffix, s.Padding, s.Reset, s.Gapless, s.PerBranch,
            NumberPattern.Format(s.Prefix, s.Suffix, s.Padding, context.Today, branch?.Code, value), value, branch?.Id, context.Today, branches,
            counters.Select(c => new SequenceCounter(c.BranchId, c.BranchId is { } b ? codes.GetValueOrDefault(b) : null, NumberPattern.PeriodText(c.PeriodYear, c.PeriodMonth), c.NextValue, c.LastIssued, c.LastIssuedAt)).ToList(),
            recent, s.CreatedAt, s.UpdatedAt, s.Version, canUpdate);
    }
}
