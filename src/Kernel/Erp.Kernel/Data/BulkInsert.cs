using NpgsqlTypes;

namespace Erp.Kernel.Data;

public sealed record BulkColumn(string Name, NpgsqlDbType Type);

/// <summary>
/// Fast inserts for seeding and imports. PostgreSQL refuses <c>COPY FROM</c> into a table with
/// row-level security, so rows are binary-copied into a transaction-local staging table and then
/// moved with one <c>INSERT … SELECT</c>. That statement runs as the application role inside the
/// tenant's transaction, so row-level security checks every row and the audit trigger records it.
/// Loads 100,000 rows in seconds.
/// </summary>
public static class BulkInsert
{
    public static async Task<long> InsertAsync(
        ErpDbSession session,
        string schema,
        string table,
        IReadOnlyList<BulkColumn> columns,
        IEnumerable<object?[]> rows,
        CancellationToken cancellationToken = default)
    {
        if (!session.HasTenant)
        {
            throw new TenantContextMissingException();
        }
        if (columns.Count == 0)
        {
            throw new ArgumentException("No columns.", nameof(columns));
        }
        var target = TenantSql.Qualified(schema, table);
        var columnList = string.Join(", ", columns.Select(c => TenantSql.Identifier(c.Name)));
        var staging = $"bulk_{Guid.NewGuid():N}";

        await Execute(session, $"CREATE TEMP TABLE {staging} ON COMMIT DROP AS SELECT {columnList} FROM {target} WITH NO DATA", cancellationToken);

        await using (var writer = await session.Connection.BeginBinaryImportAsync(
            $"COPY {staging} ({columnList}) FROM STDIN (FORMAT BINARY)", cancellationToken))
        {
            foreach (var row in rows)
            {
                if (row.Length != columns.Count)
                {
                    throw new ArgumentException($"Row has {row.Length} values, expected {columns.Count}.", nameof(rows));
                }
                await writer.StartRowAsync(cancellationToken);
                for (var i = 0; i < row.Length; i++)
                {
                    if (row[i] is null)
                    {
                        await writer.WriteNullAsync(cancellationToken);
                    }
                    else
                    {
                        await writer.WriteAsync(row[i], columns[i].Type, cancellationToken);
                    }
                }
            }
            await writer.CompleteAsync(cancellationToken);
        }

        var inserted = await Execute(session, $"INSERT INTO {target} ({columnList}) SELECT {columnList} FROM {staging}", cancellationToken);
        await Execute(session, $"DROP TABLE {staging}", cancellationToken);
        return inserted;
    }

    private static async Task<int> Execute(ErpDbSession session, string sql, CancellationToken cancellationToken)
    {
        await using var command = session.Connection.CreateCommand();
        command.Transaction = session.Transaction;
        command.CommandText = sql;
        command.CommandTimeout = 600;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
