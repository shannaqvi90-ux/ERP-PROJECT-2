using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1, every shape of every answer (critic p06 round 1). A GET whose API document enumerates how
/// it answers — a format (json, pdf, csv, xlsx), a language, digits, a disposition, a grouping —
/// produces a different artefact for each shape, and a product may keep each artefact apart from
/// the others: a printed PDF or an exported workbook kept on disk or in memory by its download name
/// ("Users 2026-10-05.ar.pdf", the same name for every tenant). The phases before never fill such a
/// store from tenant B, because tenant B never asks for a PDF or a workbook, so the planted store of
/// round 1 (plant L2) passed every gate.
///
/// Here tenant B asks for every shape of every such route on its own records (with no query and
/// with a query of its own) and must be answered; tenant A then asks for the same shapes on its own
/// records (with no query, the same query as tenant B's, and with a query of its own) while tenant
/// B keeps asking in the background; then tenant B asks once more. Every answer tenant A gets
/// (PDF text, workbook cells, CSV, JSON and every header) is judged for tenant B's markers, and
/// every answer tenant B gets for tenant A's.
/// </summary>
public static partial class IsolationAttack
{
    /// <summary>Shapes asked for per endpoint: every combination when there are at most this many,
    /// otherwise a covering set in which every combination of any three parameters' values occurs.</summary>
    private const int AllShapesUpTo = 64;

    private sealed record ShapeReport(int Endpoints, int Shapes, int AttackerRequests, int VictimRequests);

    private sealed record ShapePlan(ApiEndpoint Endpoint, string VictimPath, string AttackerPath, IReadOnlyList<string> Shapes,
        string? VictimQuery, string? AttackerQuery);

    private static async Task<ShapeReport> AnswerShapesPhaseAsync(IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi, Attacker admin, AttackState state,
        TenantSnapshot own, TenantSnapshot victim, TenantActivity activity)
    {
        var ownRouteValues = own.IdsByTable.Values.Select(ids => ids.FirstOrDefault()).Where(id => id != Guid.Empty).Select(id => id.ToString()).ToList();
        var plans = new List<ShapePlan>();
        var victimRequests = 0;
        foreach (var endpoint in endpoints.Where(e => e.Method == "GET" && !e.Pattern.Contains("{*", StringComparison.Ordinal)))
        {
            var query = openApi.Parameters("GET", endpoint.Pattern).Where(p => p.In == "query").ToList();
            var enumerated = query.Where(p => p.Enum is { Count: >= 2 }).Select(p => (p.Name, Values: (IReadOnlyList<string>)p.Enum!.Distinct(StringComparer.Ordinal).ToList())).ToList();
            if (enumerated.Count == 0)
            {
                continue;
            }
            var victimPath = await activity.OwnPathForAsync(endpoint, victim);
            var ownRoute = endpoint.RouteParameters.Count == 0 ? "" : await PickOwnRouteValueAsync(admin, endpoint, ownRouteValues);
            var attackerPath = endpoint.Path(_ => ownRoute);

            // A parameter that names a record (a report's company) must name one of the asking
            // tenant's own records, or the route answers 400 or 404 and no artefact is made.
            var ids = query.Where(p => p.Format == "uuid").Select(p => p.Name).ToList();
            var (victimBase, tries) = await OwnRecordQueryAsync(victimPath, ids, victim, path => activity.ReadPathAsync(path, "tenant B finds its own record for every answer shape"));
            victimRequests += tries;
            if (victimBase is null)
            {
                activity.NoteBlindSpot($"answer shapes: tenant B could not open {endpoint.Key} on its own records, so it made none of its shapes");
                continue;
            }
            var (attackerBase, _) = await OwnRecordQueryAsync(attackerPath, ids, own, async path =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                return await state.SendAsync(admin, endpoint, request, $"{path} [tenant A finds its own record for every answer shape]");
            });
            if (attackerBase is null)
            {
                activity.NoteBlindSpot($"answer shapes: tenant A could not open {endpoint.Key} on its own records, so it asked for none of the shapes");
                continue;
            }

            // A query of each tenant's own (a search for its own code) where the route takes free
            // text: a store keyed without the query hands the other tenant's answer to a different
            // query as well as to the same one.
            string? victimQuery = null;
            string? attackerQuery = null;
            foreach (var text in query.Where(p => p.Type == "string" && p.Format is null && p.Enum is null))
            {
                var candidate = $"{Uri.EscapeDataString(text.Name)}={Uri.EscapeDataString(victim.Code)}";
                victimRequests++;
                if (await activity.ReadPathAsync(Join(victimBase, candidate), "tenant B tries a query of its own") is >= 200 and < 300)
                {
                    victimQuery = candidate;
                    attackerQuery = $"{Uri.EscapeDataString(text.Name)}={Uri.EscapeDataString(own.Code)}";
                    break;
                }
            }

            var shapes = Shapes(enumerated);
            plans.Add(new ShapePlan(endpoint, victimBase, attackerBase, shapes, victimQuery, attackerQuery));
        }

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = AttackParallelism.Requests };
        // Tenant B's own shapes two at a time: every one renders a whole document (PDF, workbook),
        // and tenant A's requests and B's background reader share the processors with them.
        var victimParallel = new ParallelOptions { MaxDegreeOfParallelism = 2 };

        List<string> VictimPaths(ShapePlan plan) =>
            plan.Shapes.SelectMany(shape => new[] { Join(plan.VictimPath, shape), plan.VictimQuery is null ? null : Join(Join(plan.VictimPath, shape), plan.VictimQuery) })
                .OfType<string>().ToList();

        // Before: tenant B asks for every shape, and every one must be answered.
        foreach (var plan in plans)
        {
            await Parallel.ForEachAsync(VictimPaths(plan), victimParallel, async (path, _) =>
            {
                Interlocked.Increment(ref victimRequests);
                // Every actor of tenant B asks: an administrator and a read-only user may each be
                // the one whose artefact is kept.
                var status = await activity.ReadPathAsync(path, "tenant B asks for every answer shape before tenant A", everyActor: true);
                if (status is < 200 or >= 300)
                {
                    activity.NoteBlindSpot($"answer shapes: tenant B was refused {path} ({status}), so that shape held nothing of tenant B's");
                }
            });
        }

        // During: tenant A asks for the same shapes, with no query, with tenant B's very query and
        // with a query of its own, while tenant B keeps asking.
        var attackerRequests = 0;
        using (var background = new CancellationTokenSource())
        {
            var victimLoop = Task.Run(async () =>
            {
                var sent = 0;
                while (!background.IsCancellationRequested)
                {
                    foreach (var path in plans.SelectMany(VictimPaths))
                    {
                        if (background.IsCancellationRequested) break;
                        await activity.ReadPathAsync(path, "tenant B asks for answer shapes while tenant A does");
                        sent++;
                    }
                }
                return sent;
            });
            var work = new List<(ApiEndpoint Endpoint, string Path)>();
            foreach (var plan in plans)
            {
                foreach (var shape in plan.Shapes)
                {
                    var path = Join(plan.AttackerPath, shape);
                    work.Add((plan.Endpoint, path));
                    if (plan.VictimQuery is not null)
                    {
                        work.Add((plan.Endpoint, Join(path, plan.VictimQuery)));
                        work.Add((plan.Endpoint, Join(path, plan.AttackerQuery!)));
                    }
                }
            }
            await Parallel.ForEachAsync(work, parallel, async (item, _) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, item.Path);
                await state.SendAsync(admin, item.Endpoint, request, $"{item.Path} [an answer shape tenant B asked for first]",
                    item.Path.Contains(Uri.EscapeDataString(victim.Code), StringComparison.Ordinal) ? [victim.Code] : null);
                Interlocked.Increment(ref attackerRequests);
            });
            await background.CancelAsync();
            var concurrent = await victimLoop;
            victimRequests += concurrent;
            if (plans.Count > 0 && concurrent == 0)
            {
                activity.NoteBlindSpot("answer shapes: tenant B asked for nothing while tenant A asked for every shape");
            }
        }

        // After: tenant B asks for every shape once more; what tenant A's requests left behind must
        // not reach it.
        foreach (var plan in plans)
        {
            await Parallel.ForEachAsync(VictimPaths(plan), victimParallel, async (path, _) =>
            {
                Interlocked.Increment(ref victimRequests);
                await activity.ReadPathAsync(path, "tenant B asks for every answer shape after tenant A");
            });
        }
        return new ShapeReport(plans.Count, plans.Sum(p => p.Shapes.Count), attackerRequests, victimRequests);
    }

    /// <summary>The query that makes the route answer for one of the tenant's own records: none when
    /// it answers without one, else the first of the tenant's ids (the tables named like the
    /// parameter first) that every record-naming parameter accepts. Null when nothing answers.</summary>
    private static async Task<(string? Path, int Tries)> OwnRecordQueryAsync(string path, IReadOnlyList<string> idParameters, TenantSnapshot tenant, Func<string, Task<int>> get)
    {
        var tries = 1;
        if (await get(path) is >= 200 and < 300)
        {
            return (path, tries);
        }
        if (idParameters.Count == 0)
        {
            return (null, tries);
        }
        var stem = idParameters[0].ToLowerInvariant();
        var candidates = tenant.IdsByTable
            .OrderByDescending(t => t.Key.Split('.').Last().StartsWith(stem.Length > 4 ? stem[..4] : stem, StringComparison.OrdinalIgnoreCase))
            .SelectMany(t => t.Value.Take(3)).Append(tenant.TenantId).Distinct().Take(60);
        foreach (var id in candidates)
        {
            var candidate = Join(path, string.Join("&", idParameters.Select(p => $"{Uri.EscapeDataString(p)}={id}")));
            tries++;
            if (await get(candidate) is >= 200 and < 300)
            {
                return (candidate, tries);
            }
        }
        return (null, tries);
    }

    private static string Join(string path, string query) =>
        query.Length == 0 ? path : path + (path.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query;

    /// <summary>The shapes to ask for, as query strings: every combination of the enumerated values
    /// when there are few, otherwise a covering set built greedily (deterministic) in which every
    /// combination of the values of any three parameters occurs at least once — so every
    /// format with every language with every digit system, whatever else varies.</summary>
    internal static IReadOnlyList<string> Shapes(IReadOnlyList<(string Name, IReadOnlyList<string> Values)> parameters)
    {
        var rows = Cover(parameters.Select(p => p.Values.Count).ToList(), strength: 3, everyUpTo: AllShapesUpTo);
        return rows.Select(row => string.Join("&", row.Select((v, i) => $"{Uri.EscapeDataString(parameters[i].Name)}={Uri.EscapeDataString(parameters[i].Values[v])}"))).ToList();
    }

    /// <summary>Rows of value indexes (one per parameter): all of them when their number is at most
    /// <paramref name="everyUpTo"/>, otherwise a greedy covering array of the given strength.</summary>
    internal static IReadOnlyList<int[]> Cover(IReadOnlyList<int> sizes, int strength, int everyUpTo)
    {
        var product = sizes.Aggregate(1L, (n, s) => n * s);
        IEnumerable<int[]> Every()
        {
            var row = new int[sizes.Count];
            for (long n = 0; n < product; n++)
            {
                var rest = n;
                for (var i = sizes.Count - 1; i >= 0; i--)
                {
                    row[i] = (int)(rest % sizes[i]);
                    rest /= sizes[i];
                }
                yield return (int[])row.Clone();
            }
        }
        if (product <= everyUpTo)
        {
            return Every().ToList();
        }
        var t = Math.Min(strength, sizes.Count);
        var combinations = Combinations(sizes.Count, t).ToList();
        string Tuple(int[] columns, int[] row) => string.Join("|", columns.Select(c => $"{c}={row[c]}"));
        var uncovered = new HashSet<string>(StringComparer.Ordinal);
        var random = new Random(20261006);
        var pool = product <= 8192 ? Every().ToList() : null;
        foreach (var row in pool ?? Every().Take(8192).ToList())
        {
            foreach (var columns in combinations)
            {
                uncovered.Add(Tuple(columns, row));
            }
        }
        if (pool is null)
        {
            // Too many combinations to list: every tuple still has to be covered, so they are listed
            // from the parameters' sizes directly.
            uncovered.Clear();
            foreach (var columns in combinations)
            {
                var size = columns.Aggregate(1, (n, c) => n * sizes[c]);
                for (var n = 0; n < size; n++)
                {
                    var row = new int[sizes.Count];
                    var rest = n;
                    foreach (var c in columns.Reverse())
                    {
                        row[c] = rest % sizes[c];
                        rest /= sizes[c];
                    }
                    uncovered.Add(Tuple(columns, row));
                }
            }
        }
        var chosen = new List<int[]>();
        while (uncovered.Count > 0)
        {
            var candidates = pool ?? Enumerable.Range(0, 400).Select(_ => sizes.Select(s => random.Next(s)).ToArray()).ToList();
            var best = candidates.MaxBy(row => combinations.Count(columns => uncovered.Contains(Tuple(columns, row))))!;
            var gained = combinations.Where(columns => uncovered.Remove(Tuple(columns, best))).Count();
            if (gained == 0)
            {
                // Only reachable with random candidates: take the first uncovered tuple as a row.
                var first = uncovered.First().Split('|').Select(part => part.Split('=')).ToDictionary(p => int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));
                best = sizes.Select((_, i) => first.GetValueOrDefault(i)).ToArray();
                foreach (var columns in combinations) uncovered.Remove(Tuple(columns, best));
            }
            chosen.Add(best);
        }
        return chosen;
    }

    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var indexes = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])indexes.Clone();
            var i = k - 1;
            while (i >= 0 && indexes[i] == n - k + i) i--;
            if (i < 0) yield break;
            indexes[i]++;
            for (var j = i + 1; j < k; j++) indexes[j] = indexes[j - 1] + 1;
        }
    }
}
