using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Kernel.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>
/// Self-test plants for the list volume check (<see cref="G1.ListVolume"/>): the shapes a "big list"
/// optimisation takes (critic p05 round 9, plant L12), each only past <see cref="Threshold"/> rows, so
/// the gate fixture's tens of rows never reach them. On the users list only, the product answers and
/// the plant then changes the total it answered when that total is past the threshold:
/// <list type="bullet">
/// <item>V1, a query with no search and no filter: pg_class.reltuples of identity.users, the
/// planner's row count of the whole table (every tenant's rows), as L12 did;</item>
/// <item>V2, a filtered query: the total the first tenant to send that query got (a total memo for
/// queries too big to count every time, keyed by the query without the tenant);</item>
/// <item>V3, a one-letter search: the planner's row estimate of the search from EXPLAIN (whole-table
/// statistics, which row-level security does not narrow);</item>
/// <item>V4 (critic p05 round 12, plant L13), every other search, too broad to rank or ranked, alone or
/// with a filter, sort or grouping: the total the first tenant to send that search got (L13 kept the
/// total of a broad initials search, one word of 2 to 5 letters, in the temporary folder keyed by
/// list and word). Each branch of the search engine (<see cref="G1.ListVolume.SearchBranch"/>) is
/// planted, ranked answers too, so the self-test proves the check judges every branch.</item>
/// </list>
/// Hosted only by the volume self-test's planted host, over the volume environment's database; the
/// catalog figures are read with the superuser connection the self-test hands it (the application
/// role can no longer read reltuples).
/// </summary>
public sealed class VolumePlantModule : ErpModule
{
    public const string ConnectionSetting = "Erp:Testing:VolumePlantConnection";

    /// <summary>Totals past this many rows are planted (L12's threshold).</summary>
    public const int Threshold = 10_000;

    public override string Name => "volumeplant";

    public override void Register(ModuleBuilder module)
    {
        var connection = module.Configuration[ConnectionSetting] ?? throw new InvalidOperationException($"{ConnectionSetting} is not set");
        module.Services.AddSingleton<IStartupFilter>(new Filter(connection));
    }

    /// <summary>Settings that host the plants over <paramref name="env"/>'s database.</summary>
    public static Dictionary<string, string?> Settings(Erp.Testing.ErpTestEnvironment env)
    {
        var modules = env.Factory.Settings.TryGetValue("Erp:Testing:ExtraModules", out var existing) && !string.IsNullOrEmpty(existing) ? existing + ";" : "";
        return new Dictionary<string, string?>
        {
            ["Erp:Testing:ExtraModules"] = modules + typeof(VolumePlantModule).AssemblyQualifiedName,
            [ConnectionSetting] = env.AdminConnectionString,
        };
    }

    private sealed class Filter(string connection) : IStartupFilter
    {
        private readonly ConcurrentDictionary<string, int> _totals = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _searchTotals = new(StringComparer.Ordinal);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path.Value != "/api/identity/users")
                {
                    await following();
                    return;
                }
                var body = context.Response.Body;
                using var buffer = new MemoryStream();
                context.Response.Body = buffer;
                try
                {
                    await following();
                }
                finally
                {
                    context.Response.Body = body;
                }
                var bytes = buffer.ToArray();
                if (context.Response.StatusCode == StatusCodes.Status200OK && JsonNode.Parse(bytes) is JsonObject page &&
                    page["total"]?.GetValue<int>() is { } total)
                {
                    var query = context.Request.Query;
                    var search = query["search"].ToString();
                    var filter = query["filter"].ToString();
                    var branch = search.Length > 0 ? G1.ListVolume.SearchBranch(search, initials: true) : null;
                    int? planted = branch is not null and not ("one letter" or "Arabic letter")
                        ? _searchTotals.GetOrAdd($"{search}|{filter}|{query["sort"]}|{query["groupBy"]}", total)
                        : total <= Threshold ? null
                        : search.Length > 0 ? await EstimateAsync(search)
                        : filter.Length > 0 ? _totals.GetOrAdd($"{filter}|{query["sort"]}|{query["groupBy"]}", total)
                        : await WholeTableAsync();
                    if (planted is { } changed)
                    {
                        page["total"] = changed;
                        bytes = JsonSerializer.SerializeToUtf8Bytes(page);
                        context.Response.ContentLength = bytes.Length;
                    }
                }
                await body.WriteAsync(bytes);
            });
            next(app);
        };

        private async Task<int> WholeTableAsync()
        {
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT reltuples::int FROM pg_class WHERE oid = 'identity.users'::regclass", db);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        private async Task<int> EstimateAsync(string search)
        {
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var command = new NpgsqlCommand(
                "EXPLAIN (FORMAT JSON) SELECT 1 FROM identity.users WHERE display_name ILIKE @p OR email_normalized ILIKE @p", db);
            command.Parameters.AddWithValue("p", "%" + search + "%");
            var plan = JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!;
            return (int)plan[0]!["Plan"]!["Plan Rows"]!.GetValue<double>();
        }
    }
}
