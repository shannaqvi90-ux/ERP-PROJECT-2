using System.Data;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Erp.Kernel.Data;

/// <summary>The tenant and actor the current unit of work runs for.</summary>
public interface ITenantContext
{
    /// <summary>True once the unit of work is bound to a tenant.</summary>
    bool HasTenant { get; }

    /// <summary>The tenant. Throws when no tenant is bound (fail closed).</summary>
    Guid TenantId { get; }

    /// <summary>The signed-in user, or null for system work (seeding, jobs).</summary>
    Guid? ActorId { get; }
}

/// <summary>
/// One PostgreSQL connection and one transaction per unit of work (an HTTP request, a job, a seed
/// step). Every module's DbContext in the scope shares it, so a request commits or rolls back as a
/// whole and audit rows are written in the same transaction. The tenant is set with
/// <c>set_config('app.tenant_id', …, true)</c> (transaction-local) together with
/// <c>app.tenant_tx</c>, the transaction's start time; row-level security reads the tenant only
/// inside that same transaction.
/// Nothing can query tenant data before <see cref="BeginAsync"/> has run: the command guard
/// refuses, and row-level security would return nothing anyway.
/// </summary>
public sealed class ErpDbSession : ITenantContext, ICompanyContext, IAsyncDisposable
{
    private Guid? _tenantId;
    private CompanyScopeState _companies = CompanyScopeState.Nothing;
    private bool _companiesBound;
    private IReadOnlySet<Guid> _branchLimitedCompanies = new HashSet<Guid>();
    private bool _branchLimitsSet;
    private readonly IHttpContextAccessor? _http;

    /// <param name="http">The request this unit of work serves, if any: inside a request to a
    /// permissioned endpoint only the signed-in user's tenant may be bound (see
    /// <see cref="TenantBinding"/>).</param>
    public ErpDbSession(NpgsqlDataSource dataSource, IHttpContextAccessor? http = null)
    {
        _connection = dataSource.CreateConnection();
        _http = http;
    }

    private NpgsqlConnection _connection;
    private bool _connectionHandedOut;

    /// <summary>The unit of work's connection. Every DbContext and command of the scope uses it.</summary>
    public NpgsqlConnection Connection
    {
        get
        {
            _connectionHandedOut = true;
            return _connection;
        }
    }

    /// <summary>
    /// Bulk work (seeding, imports): take the connection from the bulk pool instead, whose
    /// commands inherit a long timeout (<c>ErpDataSources.BuildBulk</c>). Only before the
    /// connection has been handed to anything (a DbContext or a command), so every statement of the
    /// unit of work runs on the same connection and in the same transaction.
    /// </summary>
    public async Task UseBulkConnectionAsync(NpgsqlDataSource bulkDataSource)
    {
        ArgumentNullException.ThrowIfNull(bulkDataSource);
        if (_connectionHandedOut || Transaction is not null || _connection.State != ConnectionState.Closed)
        {
            throw new InvalidOperationException("The bulk connection must be chosen before the unit of work uses its connection.");
        }
        await _connection.DisposeAsync();
        _connection = bulkDataSource.CreateConnection();
    }

    /// <summary>Seconds a command on this unit of work's connection may run (from its pool).</summary>
    public int CommandTimeoutSeconds => _connection.CommandTimeout;

    public NpgsqlTransaction? Transaction { get; private set; }

    public bool IsActive => Transaction is not null;

    public bool HasTenant => _tenantId.HasValue && IsActive;

    public Guid TenantId => _tenantId is { } id && IsActive
        ? id
        : throw new TenantContextMissingException();

    public Guid? ActorId { get; private set; }

    /// <summary>Kind of actor recorded in the audit trail: user, seed, job, system.</summary>
    public string ActorKind { get; private set; } = "none";

    /// <summary>Correlation id recorded on audit rows (the request's trace identifier).</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Bind the unit of work to a tenant: opens the connection, begins the transaction and
    /// sets the transaction-local tenant and actor settings. Binding twice to the same tenant is a
    /// no-op; binding to a different tenant throws. The company scope starts as every company of the
    /// tenant for system work (seed, job, system) and as no company for a signed-in user, until
    /// <see cref="BindCompaniesAsync"/> narrows or sets it.</summary>
    public async Task BeginAsync(Guid tenantId, Guid? actorId, string actorKind, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is empty.", nameof(tenantId));
        }
        using var bindActivity = TenantBinding.StartBind(tenantId, actorKind);
        TenantBinding.Check(_http?.HttpContext, tenantId);
        if (Transaction is not null)
        {
            if (_tenantId == tenantId && ActorId == actorId)
            {
                return;
            }
            throw new InvalidOperationException("This unit of work is already bound to another tenant or actor.");
        }
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
        Transaction = await _connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (_http?.HttpContext is { } request && Http.ReadOnlyRequests.Applies(request))
        {
            // A request that only reads (GET, HEAD, ReadOnlyOperation): PostgreSQL refuses every
            // write in this transaction, so a read permission can never change data.
            await MakeReadOnlyAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant, true), set_config('app.tenant_tx', extract(epoch from now())::text, true), " +
            "set_config('app.actor_id', @actor, true), " +
            "set_config('app.actor_kind', @kind, true), set_config('app.correlation_id', @correlation, true), " +
            "set_config('app.company_scope', @scope, true), set_config('app.company_ids', '', true), " +
            "set_config('app.company_tx', extract(epoch from now())::text, true)",
            _connection, Transaction))
        {
            command.Parameters.AddWithValue("tenant", tenantId.ToString());
            command.Parameters.AddWithValue("actor", actorId?.ToString() ?? string.Empty);
            command.Parameters.AddWithValue("kind", actorKind);
            command.Parameters.AddWithValue("correlation", CorrelationId ?? string.Empty);
            command.Parameters.AddWithValue("scope", actorKind == UserActorKind ? "none" : "all");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        _tenantId = tenantId;
        ActorId = actorId;
        ActorKind = actorKind;
        _companies = actorKind == UserActorKind ? CompanyScopeState.Nothing : CompanyScopeState.Everything;
        _companiesBound = false;
        _branchLimitedCompanies = new HashSet<Guid>();
        _branchLimitsSet = false;
    }

    /// <summary>Actor kind of a signed-in user; such a unit of work starts with no company.</summary>
    public const string UserActorKind = "user";

    /// <summary>
    /// Set the companies this unit of work may touch, once, after the tenant is bound. Row-level
    /// security (<c>company_scope</c> policies) and the company query filter then show only rows of
    /// these companies. A second call throws, so code running later in the request cannot widen
    /// what the session allowed.
    /// </summary>
    public async Task BindCompaniesAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken cancellationToken = default)
    {
        if (!HasTenant)
        {
            throw new TenantContextMissingException();
        }
        if (_companiesBound)
        {
            throw new InvalidOperationException("The company scope of this unit of work is already bound.");
        }
        var ids = companyIds.Distinct().ToArray();
        await SetCompanySettingsAsync(ids, cancellationToken);
        _companies = new CompanyScopeState(false, ids, null, null, []);
        _companiesBound = true;
    }

    /// <summary>Record the company and branch the user is working in, and the branches they may
    /// work in (all within the bound companies). Informational for modules: the default company
    /// of new records and lists. Security comes from <see cref="BindCompaniesAsync"/>.</summary>
    public void SetWorkplace(Guid? activeCompanyId, Guid? activeBranchId, IReadOnlyCollection<Guid> branchIds)
    {
        if (activeCompanyId is { } active && !AllowsCompany(active))
        {
            throw new ArgumentException("The working company must be in the company scope.", nameof(activeCompanyId));
        }
        _companies = _companies with { ActiveCompanyId = activeCompanyId, ActiveBranchId = activeBranchId, BranchIds = branchIds.Distinct().ToArray() };
    }

    /// <summary>
    /// Add one company to a user's scope: the company this unit of work has just created (its
    /// creator works in it from then on). Only a brand-new id is acceptable here; callers must
    /// create the company in the same transaction.
    /// </summary>
    public async Task IncludeNewCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (!HasTenant)
        {
            throw new TenantContextMissingException();
        }
        if (_companies.All || _companies.CompanyIds.Contains(companyId))
        {
            return;
        }
        var ids = _companies.CompanyIds.Append(companyId).ToArray();
        await SetCompanySettingsAsync(ids, cancellationToken);
        _companies = _companies with { CompanyIds = ids };
    }

    private async Task SetCompanySettingsAsync(Guid[] ids, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('app.company_scope', 'list', true), set_config('app.company_ids', @ids, true), " +
            "set_config('app.company_tx', extract(epoch from now())::text, true)", Connection, Transaction);
        command.Parameters.AddWithValue("ids", string.Join(',', ids));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public bool AllCompanies => HasTenant && _companies.All;

    public IReadOnlyList<Guid> CompanyIds => HasTenant ? _companies.CompanyIds : [];

    public Guid? ActiveCompanyId => HasTenant ? _companies.ActiveCompanyId : null;

    public Guid? ActiveBranchId => HasTenant ? _companies.ActiveBranchId : null;

    public IReadOnlyList<Guid> BranchIds => HasTenant ? _companies.BranchIds : [];

    public bool AllowsCompany(Guid companyId) => HasTenant && (_companies.All || _companies.CompanyIds.Contains(companyId));

    /// <summary>
    /// Record the companies of the scope where the user may work in only some branches, once, after
    /// <see cref="BindCompaniesAsync"/>. In those companies the user reads the records every branch
    /// shares (<see cref="ICompanyWide"/>) but never writes them. A second call throws, so code
    /// running later in the request cannot lift the limits.
    /// </summary>
    public void SetBranchLimits(IEnumerable<Guid> branchLimitedCompanyIds)
    {
        if (!HasTenant)
        {
            throw new TenantContextMissingException();
        }
        if (_branchLimitsSet)
        {
            throw new InvalidOperationException("The branch limits of this unit of work are already set.");
        }
        _branchLimitedCompanies = branchLimitedCompanyIds.ToHashSet();
        _branchLimitsSet = true;
    }

    public bool HoldsEveryBranch(Guid companyId) => AllowsCompany(companyId) && (_companies.All || !_branchLimitedCompanies.Contains(companyId));

    private sealed record CompanyScopeState(bool All, IReadOnlyList<Guid> CompanyIds, Guid? ActiveCompanyId, Guid? ActiveBranchId, IReadOnlyList<Guid> BranchIds)
    {
        public static readonly CompanyScopeState Nothing = new(false, [], null, null, []);
        public static readonly CompanyScopeState Everything = new(true, [], null, null, []);
    }

    /// <summary>Open the connection without binding a tenant, for the few reviewed
    /// security-definer lookups that resolve a tenant (sign-in, session). Tenant tables return
    /// nothing on this connection.</summary>
    public async Task<NpgsqlConnection> OpenUnboundAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is not null)
        {
            throw new InvalidOperationException("The unit of work is already bound to a tenant.");
        }
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
        _connectionHandedOut = true;
        return _connection;
    }

    /// <summary>Make the current transaction read-only: PostgreSQL then refuses every write until
    /// it ends. Requests that only read (GET, HEAD and endpoints marked read-only) run this way, so
    /// a read permission can never change data.</summary>
    public async Task MakeReadOnlyAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is null)
        {
            return;
        }
        using var activity = TenantBinding.StartReadOnly();
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", _connection, Transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is null)
        {
            return;
        }
        await Transaction.CommitAsync(cancellationToken);
        await Transaction.DisposeAsync();
        Transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is null)
        {
            return;
        }
        try
        {
            await Transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null)
        {
            try
            {
                await Transaction.RollbackAsync();
            }
            catch (Exception) when (_connection.State != ConnectionState.Open)
            {
                // Connection already broken; nothing to roll back.
            }
            await Transaction.DisposeAsync();
            Transaction = null;
        }
        await _connection.DisposeAsync();
    }
}

/// <summary>
/// The companies the current unit of work may touch. Companies divide a tenant's data: a row that
/// belongs to a company carries <c>company_id</c> and is visible only inside this scope (row-level
/// security, plus the query filter of <see cref="ICompanyOwned"/> entities). A signed-in user's
/// scope is the companies they were given access to; system work (seeding, jobs, operator
/// commands) sees every company of its tenant.
/// </summary>
public interface ICompanyContext
{
    /// <summary>True for system work that sees every company of the tenant.</summary>
    bool AllCompanies { get; }

    /// <summary>The companies a user may work in (empty for system work, see <see cref="AllCompanies"/>).</summary>
    IReadOnlyList<Guid> CompanyIds { get; }

    /// <summary>The company the user is working in now: the default for new records and lists.</summary>
    Guid? ActiveCompanyId { get; }

    /// <summary>The branch the user is working in now.</summary>
    Guid? ActiveBranchId { get; }

    /// <summary>The branches the user may work in (of the allowed companies).</summary>
    IReadOnlyList<Guid> BranchIds { get; }

    bool AllowsCompany(Guid companyId);

    /// <summary>True when the company is in the scope and the user may work in every branch of it
    /// (always, for system work). Records every branch shares (<see cref="ICompanyWide"/>) are
    /// written only where this holds.</summary>
    bool HoldsEveryBranch(Guid companyId);
}

/// <summary>A row that belongs to one company of its tenant (column <c>company_id</c>).</summary>
public interface ICompanyOwned : ITenantOwned
{
    Guid CompanyId { get; set; }
}

/// <summary>
/// A row every branch of its company shares: the company's own record, and later its settings,
/// chart of accounts or price lists. A user who may work in only some branches of the company reads
/// it but never adds, changes or deletes it: <see cref="ModuleDbContext"/> refuses the write
/// (<see cref="CrossBranchWriteException"/>, answered 403) whatever the endpoint checked, and the
/// G1 branch attack attacks every table of such rows.
/// </summary>
public interface ICompanyWide : ICompanyOwned
{
}

public sealed class TenantContextMissingException()
    : InvalidOperationException("Tenant data was accessed outside a tenant-bound unit of work.");
