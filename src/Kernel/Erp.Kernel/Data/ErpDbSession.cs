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
public sealed class ErpDbSession : ITenantContext, IAsyncDisposable
{
    private Guid? _tenantId;
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
    /// commands inherit a long timeout (<see cref="ErpDataSources.BuildBulk"/>). Only before the
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
    /// no-op; binding to a different tenant throws.</summary>
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
            "set_config('app.actor_kind', @kind, true), set_config('app.correlation_id', @correlation, true)",
            _connection, Transaction))
        {
            command.Parameters.AddWithValue("tenant", tenantId.ToString());
            command.Parameters.AddWithValue("actor", actorId?.ToString() ?? string.Empty);
            command.Parameters.AddWithValue("kind", actorKind);
            command.Parameters.AddWithValue("correlation", CorrelationId ?? string.Empty);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        _tenantId = tenantId;
        ActorId = actorId;
        ActorKind = actorKind;
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

public sealed class TenantContextMissingException()
    : InvalidOperationException("Tenant data was accessed outside a tenant-bound unit of work.");
