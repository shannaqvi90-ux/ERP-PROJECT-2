using System.Text;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Security;
using Erp.Testing;
using Npgsql;

namespace Erp.Gates.Tests.G2;

/// <summary>One id that belongs to the caller, and where the gate found it.</summary>
/// <param name="Sources">Every place the id was found, the first one naming it in findings.</param>
public sealed record OwnId(Guid Value, IReadOnlyList<string> Sources)
{
    public string Source => Sources[0];

    public override string ToString() => $"{string.Join(" and ", Sources)} {Value}";
}

/// <summary>
/// The ids a permission decision might wrongly treat as "the caller's own" (critic p00 round 11,
/// plant P6: "people may always manage their own record" let a user whose roles grant nothing read
/// and change their own user record, while every matrix sent random ids and passed). Read from the
/// database as the superuser, never from the application: the caller's user id, their tenant, the
/// session the request runs in, and every id held by the rows of every tenant table that mention
/// the caller (their user row, their role and company assignments, their companies and branches,
/// their sessions, sign-ins and passkeys, the records they created or own, the audit entries about
/// them). Self-service screens to come (my payslip, my expense claims, my leave) add rows that
/// mention the caller, so their ids join this set from the day the table exists.
/// </summary>
public static class OwnIds
{
    /// <summary>Rows read per tenant table (the latest first): enough to hold each kind of id the
    /// caller has in that table, few enough that the matrices stay cheap.</summary>
    public const int RowsPerTable = 6;

    public static async Task<IReadOnlyList<OwnId>> OfAsync(ErpTestEnvironment env, Guid userId, string token)
    {
        await using var connection = await env.OpenAdminAsync();
        // Every place an id was found: the same id is often both a record's own id and the
        // record_id of the audit entries about it.
        var found = new Dictionary<Guid, List<string>>();
        var order = new List<Guid>();
        void Add(Guid value, string source)
        {
            if (value == Guid.Empty)
            {
                return;
            }
            if (!found.TryGetValue(value, out var sources))
            {
                found[value] = sources = [];
                order.Add(value);
            }
            if (!sources.Contains(source))
            {
                sources.Add(source);
            }
        }

        Add(userId, "the caller's user id");
        Add(await DbCatalog.ScalarAsync<Guid>(connection, "SELECT tenant_id FROM identity.users WHERE id = @u", ("u", userId)), "the caller's tenant");
        // The session the request runs in: found by the hash of the token the request carries, so
        // a missing or renamed sessions table fails here rather than leaving the session untried.
        var session = await DbCatalog.ReadAsync(connection, "SELECT id FROM identity.sessions WHERE token_hash = @h AND user_id = @u",
            r => r.GetGuid(0), ("h", SessionTokens.Hash(token)), ("u", userId));
        if (session is not [var sessionId])
        {
            throw new InvalidOperationException($"The session of the caller {userId} was not found by its token hash ({session.Count} rows): the gate cannot try it.");
        }
        Add(sessionId, "the caller's session");

        foreach (var table in await DbCatalog.TenantTablesAsync(connection))
        {
            var uuids = (await DbCatalog.ColumnsAsync(connection, table)).Where(c => c.Type == "uuid").Select(c => c.Name).ToList();
            if (uuids.Count == 0)
            {
                continue;
            }
            var select = string.Join(", ", uuids.Select(c => $"t.\"{c}\""));
            var rows = await DbCatalog.ReadAsync(connection,
                $"SELECT {select} FROM {table.Qualified} t WHERE t::text LIKE '%' || @u || '%' ORDER BY t.ctid DESC LIMIT {RowsPerTable}",
                r => Enumerable.Range(0, uuids.Count).Select(i => r.IsDBNull(i) ? Guid.Empty : r.GetGuid(i)).ToArray(),
                ("u", userId.ToString()));
            foreach (var row in rows)
            {
                for (var i = 0; i < uuids.Count; i++)
                {
                    Add(row[i], $"{table.Qualified}.{uuids[i]}");
                }
            }
        }
        return order.Select(v => new OwnId(v, found[v])).ToList();
    }

    /// <summary>
    /// The requests that name the caller's own ids: for an endpoint with route parameters, one per
    /// own id with every route parameter set to it; for one without, one naming the caller's user
    /// id. Each also names the id in the query string and, for a method with a body, in the body
    /// (<c>id</c> and <c>userId</c>), so a decision read from any of the three is tried. Reads run
    /// first and the endpoints that end sessions or remove records last, so a planted bypass that
    /// signs the caller out shows up on every request before it.
    /// </summary>
    public static IEnumerable<(ApiEndpoint Endpoint, OwnId Own, Func<HttpRequestMessage> Build)> Requests(IEnumerable<ApiEndpoint> endpoints, IReadOnlyList<OwnId> own)
    {
        foreach (var endpoint in endpoints.OrderBy(e => e.Method switch { "GET" => 0, "PUT" => 1, "PATCH" => 2, "POST" => 3, _ => 4 })
                     .ThenBy(e => e.Pattern.Contains("revoke", StringComparison.Ordinal) ? 1 : 0).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            foreach (var id in endpoint.RouteParameters.Count > 0 ? own : own.Take(1))
            {
                yield return (endpoint, id, () => Request(endpoint, id.Value));
            }
        }
    }

    private static HttpRequestMessage Request(ApiEndpoint endpoint, Guid id)
    {
        var value = id.ToString();
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), endpoint.Path(_ => value) + $"?id={value}&userId={value}");
        if (endpoint.HasBody)
        {
            request.Content = new StringContent($$"""{"id":"{{value}}","userId":"{{value}}"}""", Encoding.UTF8, "application/json");
        }
        return request;
    }

    /// <summary>The module part of a permission key (<c>identity</c> of <c>identity.users.read</c>).</summary>
    public static string ModuleOf(string permission) => permission[..permission.IndexOf('.', StringComparison.Ordinal)];
}
