using System.Data;
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
/// <c>set_config('app.tenant_id', …, true)</c> (transaction-local), which row-level security reads.
/// Nothing can query tenant data before <see cref="BeginAsync"/> has run: the command guard
/// refuses, and row-level security would return nothing anyway.
/// </summary>
public sealed class ErpDbSession : ITenantContext, IAsyncDisposable
{
    private Guid? _tenantId;

    public ErpDbSession(NpgsqlDataSource dataSource)
    {
        Connection = dataSource.CreateConnection();
    }

    public NpgsqlConnection Connection { get; }

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
        if (Transaction is not null)
        {
            if (_tenantId == tenantId && ActorId == actorId)
            {
                return;
            }
            throw new InvalidOperationException("This unit of work is already bound to another tenant or actor.");
        }
        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }
        Transaction = await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using (var command = new NpgsqlCommand(
            "SELECT set_config('app.tenant_id', @tenant, true), set_config('app.actor_id', @actor, true), " +
            "set_config('app.actor_kind', @kind, true), set_config('app.correlation_id', @correlation, true)",
            Connection, Transaction))
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
        if (Connection.State != ConnectionState.Open)
        {
            await Connection.OpenAsync(cancellationToken);
        }
        return Connection;
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
            catch (Exception) when (Connection.State != ConnectionState.Open)
            {
                // Connection already broken; nothing to roll back.
            }
            await Transaction.DisposeAsync();
            Transaction = null;
        }
        await Connection.DisposeAsync();
    }
}

public sealed class TenantContextMissingException()
    : InvalidOperationException("Tenant data was accessed outside a tenant-bound unit of work.");
