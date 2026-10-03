using Npgsql;

namespace Erp.Gates.Tests.Infrastructure;

public sealed record TableRef(string Schema, string Name)
{
    public string Qualified => $"{Schema}.{Name}";
    public override string ToString() => Qualified;
}

public sealed record ColumnInfo(string Name, string Type, bool NotNull, bool Identity, bool Generated);

/// <summary>Reads PostgreSQL's catalogue directly (independent of the application's own
/// checks).</summary>
public static class DbCatalog
{
    public const string UserSchemaFilter =
        "n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast') AND n.nspname NOT LIKE 'pg_temp%' AND n.nspname NOT LIKE 'pg_toast_temp%'";

    public static async Task<List<TableRef>> AllTablesAsync(NpgsqlConnection connection)
    {
        var sql = $"""
            SELECT n.nspname, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE c.relkind IN ('r', 'p') AND {UserSchemaFilter}
             ORDER BY 1, 2
            """;
        return await ReadAsync(connection, sql, r => new TableRef(r.GetString(0), r.GetString(1)));
    }

    public static async Task<List<TableRef>> TenantTablesAsync(NpgsqlConnection connection)
    {
        var sql = $"""
            SELECT n.nspname, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
              JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'tenant_id' AND NOT a.attisdropped
             WHERE c.relkind IN ('r', 'p') AND {UserSchemaFilter}
             ORDER BY 1, 2
            """;
        return await ReadAsync(connection, sql, r => new TableRef(r.GetString(0), r.GetString(1)));
    }

    /// <summary>Tenant tables whose rows belong to a company (a <c>company_id</c> column).</summary>
    public static async Task<List<TableRef>> CompanyTablesAsync(NpgsqlConnection connection)
    {
        var sql = $"""
            SELECT n.nspname, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
              JOIN pg_attribute t ON t.attrelid = c.oid AND t.attname = 'tenant_id' AND NOT t.attisdropped
              JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'company_id' AND NOT a.attisdropped
             WHERE c.relkind IN ('r', 'p') AND {UserSchemaFilter}
             ORDER BY 1, 2
            """;
        return await ReadAsync(connection, sql, r => new TableRef(r.GetString(0), r.GetString(1)));
    }

    public static async Task<List<ColumnInfo>> ColumnsAsync(NpgsqlConnection connection, TableRef table)
    {
        const string sql = """
            SELECT a.attname, format_type(a.atttypid, a.atttypmod), a.attnotnull, a.attidentity <> '', a.attgenerated <> ''
              FROM pg_attribute a
             WHERE a.attrelid = to_regclass(@table) AND a.attnum > 0 AND NOT a.attisdropped
             ORDER BY a.attnum
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", table.Qualified);
        var list = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4)));
        }
        return list;
    }

    public static async Task<List<T>> ReadAsync<T>(NpgsqlConnection connection, string sql, Func<NpgsqlDataReader, T> map, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        var list = new List<T>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(map(reader));
        }
        return list;
    }

    public static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return await command.ExecuteNonQueryAsync();
    }
}
