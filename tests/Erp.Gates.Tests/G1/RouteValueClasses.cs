namespace Erp.Gates.Tests.G1;

/// <summary>
/// Classes of the route values the HTTP attack sends, so a request that adds nothing to the
/// requests already sent for the same endpoint can be left out (the verify's processor-time
/// budget, decision record p00-foundation-gate-cost.md).
///
/// A route value of tenant B is one of: the id of a row of one of B's tables (class: the table),
/// B's own id (class "tenant"), B's code, an id B created while it used the app (a row of one of
/// its tables after the snapshot, class "created"), or any other text (class "text"). Two ids of
/// one table are answered by the same handler code with the same lookup: what differs between
/// them is the row. For the attack that sends the same request many times to find which
/// credential, header or query carries a tenant switch, or which caller the handler refuses, the
/// route value only has to name a row that exists in B for the lookup to reach it: a few ids of
/// each table (first, middle and last, so rows of every company and branch the seed spreads over
/// a table are among them) are as strong as all of them. The plain request of the two sessions
/// that reach the handler (cookie and bearer) is still sent with every value.
/// </summary>
public static class RouteValueClasses
{
    /// <summary>Values of each class kept as representatives: the first, the middle and the last.</summary>
    public const int PerClass = 3;

    /// <param name="values">Route values in the order the attack sends them.</param>
    /// <param name="classOf">The class of a value.</param>
    /// <returns>The values that stand for their classes (every value when a class has at most
    /// <see cref="PerClass"/> values), in the order given.</returns>
    public static IReadOnlyList<string> Representatives(IReadOnlyList<string> values, Func<string, string> classOf)
    {
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in values.GroupBy(classOf, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count <= PerClass)
            {
                chosen.UnionWith(members);
                continue;
            }
            chosen.Add(members[0]);
            chosen.Add(members[members.Count / 2]);
            chosen.Add(members[^1]);
        }
        return values.Where(chosen.Contains).ToList();
    }

    /// <summary>The class function for tenant B's route values.</summary>
    public static Func<string, string> ClassifierFor(IReadOnlyDictionary<string, IReadOnlyList<Guid>> idsByTable, Guid tenantId, string tenantCode)
    {
        var tableOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, ids) in idsByTable)
        {
            foreach (var id in ids)
            {
                tableOf[id.ToString()] = "table " + table;
            }
        }
        tableOf[tenantId.ToString()] = "tenant";
        return value => tableOf.TryGetValue(value, out var table) ? table
            : value.Equals(tenantCode, StringComparison.OrdinalIgnoreCase) ? "code"
            : Guid.TryParse(value, out _) ? "created"
            : "text";
    }
}
