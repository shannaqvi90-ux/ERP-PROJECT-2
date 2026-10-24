using Erp.Kernel.Data;
using Erp.Kernel.Http;
using Erp.Modules.Numbering.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Modules.Numbering;

/// <summary>
/// Takes document numbers for later modules (see <see cref="IDocumentNumbers"/>). One statement
/// takes the counter's value and moves it on (an insert of the period's first counter, or an update
/// of the counter, which locks its row until the transaction ends); a second records the number with
/// its document. A gapless sequence runs both in the caller's own transaction, so a second document
/// of the same sequence waits for the first to commit or roll back, and a rolled-back document gives
/// its number back. Any other sequence runs them in a short transaction of its own on a second
/// connection, bound to the caller's own tenant, actor, company and permission's company (never to
/// a value of the request), and commits at once: nobody waits, and a document whose save then fails
/// leaves a gap.
/// </summary>
internal sealed class DocumentNumbers(
    NumberingDbContext db,
    ErpDbSession session,
    DocumentTypeCatalog types,
    IBranchDirectory branches,
    IServiceScopeFactory scopes,
    IHttpContextAccessor http) : IDocumentNumbers
{
    /// <summary>Seconds a take waits for another document of the same gapless sequence.</summary>
    internal const int WaitSeconds = 10;

    internal const string TakeStatement = """
        INSERT INTO numbering.counters AS c (id, tenant_id, company_id, sequence_id, branch_id, period, next_value, last_issued, last_issued_at, created_by, updated_by)
        VALUES (@id, @tenant, @company, @sequence, @branch, @period, @first + 1, @first, now(), @actor, @actor)
        ON CONFLICT (tenant_id, sequence_id, branch_id, period) DO UPDATE
           SET next_value = c.next_value + 1, last_issued = c.next_value, last_issued_at = now(), updated_at = now(), updated_by = @actor
        RETURNING c.last_issued
        """;

    internal const string RecordStatement = """
        INSERT INTO numbering.issued_numbers (id, tenant_id, company_id, sequence_id, document_type, branch_id, period, value, number, document_date, source_id, gapless, created_by, updated_by)
        VALUES (@id, @tenant, @company, @sequence, @type, @branch, @period, @value, @number, @date, @source, @gapless, @actor, @actor)
        ON CONFLICT (tenant_id, company_id, document_type, number) DO NOTHING
        RETURNING id
        """;

    public async Task<IssuedNumber> TakeAsync(NumberRequest request, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(request, cancellationToken);
        if (http.HttpContext is { } context && ReadOnlyRequests.Applies(context))
        {
            throw new NumberingException(NumberingException.ReadOnly);
        }
        if (plan.Sequence.Gapless)
        {
            return await TakeOnAsync(session, plan, request, cancellationToken);
        }
        await using var scope = scopes.CreateAsyncScope();
        var detached = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        detached.CorrelationId = session.CorrelationId;
        // The caller's own tenant and actor, narrowed to the one company the number is for.
        await detached.BeginAsync(session.TenantId, session.ActorId, session.ActorKind, cancellationToken);
        if (!session.AllCompanies)
        {
            await detached.BindCompaniesAsync([request.CompanyId], cancellationToken);
            detached.SetBranchLimits(session.HoldsEveryBranch(request.CompanyId) ? [] : [request.CompanyId]);
        }
        if (!session.PermissionEverywhere)
        {
            await detached.LimitToPermissionAsync([request.CompanyId], narrowScope: false, cancellationToken);
        }
        var issued = await TakeOnAsync(detached, plan, request, cancellationToken);
        await detached.CommitAsync(cancellationToken);
        return issued;
    }

    public async Task<NumberPreview> PreviewAsync(NumberRequest request, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(request, cancellationToken);
        var counter = await db.Counters.AsNoTracking()
            .Where(c => c.SequenceId == plan.Sequence.Id && c.BranchId == plan.CounterBranch && c.Period == plan.Period)
            .Select(c => (long?)c.NextValue).SingleOrDefaultAsync(cancellationToken);
        var value = counter ?? 1;
        var s = plan.Sequence;
        return new NumberPreview(NumberPattern.Format(s.Prefix, s.Suffix, s.Padding, request.DocumentDate, plan.BranchCode, value), value, plan.Period, s.Gapless, s.Id, plan.CounterBranch);
    }

    /// <summary>The sequence, period and branch a request is counted in, after every check.</summary>
    private sealed record Plan(NumberSequence Sequence, string Period, Guid? CounterBranch, string? BranchCode);

    private async Task<Plan> PlanAsync(NumberRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (types.Find(request.DocumentType) is null)
        {
            throw new NumberingException(NumberingException.DocumentTypeUnknown, request.DocumentType);
        }
        if (request.DocumentDate.Year is < NumberPattern.MinYear or > NumberPattern.MaxYear)
        {
            throw new NumberingException(NumberingException.DateOutOfRange, NumberPattern.MinYear, NumberPattern.MaxYear);
        }
        if (!session.HasTenant || !session.AllowsCompany(request.CompanyId) || !session.PermitsCompany(request.CompanyId))
        {
            throw new NumberingException(NumberingException.CompanyNotAllowed);
        }
        var sequence = await db.Sequences.AsNoTracking()
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId && s.DocumentType == request.DocumentType, cancellationToken)
            ?? throw new NumberingException(NumberingException.SequenceMissing, request.DocumentType);
        Guid? counterBranch = null;
        string? branchCode = null;
        if (request.BranchId is { } branchId)
        {
            var branch = await branches.GetAsync(branchId, cancellationToken);
            if (branch is null || branch.CompanyId != request.CompanyId)
            {
                throw new NumberingException(NumberingException.BranchNotOfCompany);
            }
            branchCode = branch.Code;
        }
        if (sequence.PerBranch)
        {
            counterBranch = request.BranchId ?? throw new NumberingException(NumberingException.BranchRequired, request.DocumentType);
        }
        return new Plan(sequence, NumberPattern.PeriodOf(sequence.Reset, request.DocumentDate), counterBranch, branchCode);
    }

    private static async Task<IssuedNumber> TakeOnAsync(ErpDbSession unit, Plan plan, NumberRequest request, CancellationToken cancellationToken)
    {
        var s = plan.Sequence;
        long value;
        try
        {
            await using (var take = new NpgsqlCommand(TakeStatement, unit.Connection, unit.Transaction) { CommandTimeout = WaitSeconds })
            {
                take.Parameters.AddWithValue("id", Guid.CreateVersion7());
                take.Parameters.AddWithValue("tenant", unit.TenantId);
                take.Parameters.AddWithValue("company", request.CompanyId);
                take.Parameters.AddWithValue("sequence", s.Id);
                take.Parameters.Add(new NpgsqlParameter<Guid?>("branch", NpgsqlTypes.NpgsqlDbType.Uuid) { TypedValue = plan.CounterBranch });
                take.Parameters.AddWithValue("period", plan.Period);
                take.Parameters.AddWithValue("first", 1L);
                take.Parameters.Add(new NpgsqlParameter<Guid?>("actor", NpgsqlTypes.NpgsqlDbType.Uuid) { TypedValue = unit.ActorId });
                value = (long)(await take.ExecuteScalarAsync(cancellationToken))!;
            }
        }
        catch (NpgsqlException e) when (e.InnerException is TimeoutException || e is PostgresException { SqlState: PostgresErrorCodes.QueryCanceled or PostgresErrorCodes.LockNotAvailable })
        {
            throw new NumberingException(NumberingException.Busy);
        }
        var number = NumberPattern.Format(s.Prefix, s.Suffix, s.Padding, request.DocumentDate, plan.BranchCode, value);
        await using (var record = new NpgsqlCommand(RecordStatement, unit.Connection, unit.Transaction))
        {
            record.Parameters.AddWithValue("id", Guid.CreateVersion7());
            record.Parameters.AddWithValue("tenant", unit.TenantId);
            record.Parameters.AddWithValue("company", request.CompanyId);
            record.Parameters.AddWithValue("sequence", s.Id);
            record.Parameters.AddWithValue("type", s.DocumentType);
            record.Parameters.Add(new NpgsqlParameter<Guid?>("branch", NpgsqlTypes.NpgsqlDbType.Uuid) { TypedValue = request.BranchId });
            record.Parameters.AddWithValue("period", plan.Period);
            record.Parameters.AddWithValue("value", value);
            record.Parameters.AddWithValue("number", number);
            record.Parameters.AddWithValue("date", request.DocumentDate);
            record.Parameters.Add(new NpgsqlParameter<Guid?>("source", NpgsqlTypes.NpgsqlDbType.Uuid) { TypedValue = request.SourceId });
            record.Parameters.AddWithValue("gapless", s.Gapless);
            record.Parameters.Add(new NpgsqlParameter<Guid?>("actor", NpgsqlTypes.NpgsqlDbType.Uuid) { TypedValue = unit.ActorId });
            if (await record.ExecuteScalarAsync(cancellationToken) is null)
            {
                throw new NumberingException(NumberingException.NumberTaken, number);
            }
        }
        return new IssuedNumber(number, value, plan.Period, s.Gapless, s.Id, plan.CounterBranch);
    }
}
