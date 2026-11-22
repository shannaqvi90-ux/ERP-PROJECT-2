using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Security;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2: a read permission can never change data. GET and HEAD requests, and endpoints marked
/// read-only, run in a read-only transaction (the database refuses their writes). An endpoint
/// with any other method that declares a read action (<c>*.read</c>, <c>*.view</c>,
/// <c>*.list</c>, <c>*.search</c>) must therefore be marked read-only, or be reviewed in
/// tests/Gates/read-permission-writes.txt with the reason a reader may make that change (for
/// example saving their own view of a list they may read). Without this, an endpoint that
/// reactivates a user while declaring <c>identity.users.read</c> passes every other check: the
/// permission matrix trusts what each endpoint declares (critic p00 round 2, plant P2).
/// </summary>
public static class ReadPermissionWrites
{
    public const string ReviewedFile = "tests/Gates/read-permission-writes.txt";

    public static readonly IReadOnlySet<string> ReadActions = new HashSet<string>(StringComparer.Ordinal) { "read", "view", "list", "search" };

    public static bool ChangesData(ApiEndpoint endpoint) =>
        endpoint.Method is not ("GET" or "HEAD" or "OPTIONS") && !endpoint.ReadOnlyOperation;

    public sealed record Result(IReadOnlyList<string> Problems, int StateChangingChecked);

    /// <param name="unexercised">The endpoints the write-set gate cannot get an accepted answer from
    /// (tests/Gates/write-set-unexercised.txt), each with its reason; null reads the file.</param>
    public static Result Check(IEnumerable<ApiEndpoint> endpoints, IReadOnlyList<(string Entry, string Reason)> reviewed,
        IReadOnlyList<(string Entry, string Reason)>? unexercised = null)
    {
        var problems = new List<string>();
        // Backlog row 17 (critic p10 round 4, plant P2b): an endpoint that changes data under a read
        // permission, reviewed here AND listed as one no generic write-set request reaches, is a write
        // by a reader that no gate sends: the reviewed write set of its permission would never be
        // compared with what it writes. Refused outright; and every unexercised endpoint names the
        // test file that sends it itself, which must exist.
        unexercised ??= Repo.ReadReviewedList(WriteSets.UnexercisedFile);
        var unreached = unexercised.Select(u => u.Entry.Replace(WriteSets.WithDependents, "", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        problems.AddRange(reviewed.Where(r => unreached.Contains(r.Entry)).Select(r =>
            $"{r.Entry} is reviewed in {ReviewedFile} (a reader may change data there) and listed in {WriteSets.UnexercisedFile} (no write-set request reaches it): " +
            "a write by a read permission that no gate sends. Make the generic write-set gate reach it"));
        foreach (var (entry, reason) in unexercised)
        {
            var named = System.Text.RegularExpressions.Regex.Matches(reason, @"tests/Erp\.Gates\.Tests/[A-Za-z0-9_/]+\.cs").Select(m => m.Value).Distinct().ToList();
            if (named.Count == 0 || named.Any(f => !File.Exists(Repo.PathOf(f.Split('/')))))
            {
                problems.Add($"{WriteSets.UnexercisedFile}: '{entry}' must name, in its reason, the existing test file that sends it itself ({(named.Count == 0 ? "none named" : string.Join(", ", named.Where(f => !File.Exists(Repo.PathOf(f.Split('/'))))) + " does not exist")})");
            }
        }
        problems.AddRange(reviewed.Where(r => string.IsNullOrWhiteSpace(r.Reason)).Select(r => $"{ReviewedFile}: '{r.Entry}' needs a reason after '#'"));
        var allowed = reviewed.Select(r => r.Entry).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var checkedCount = 0;
        foreach (var endpoint in endpoints.Where(e => !e.IsAnonymous && ChangesData(e)))
        {
            checkedCount++;
            var action = PermissionDefinition.Parse(endpoint.Permission).Action;
            if (!ReadActions.Contains(action))
            {
                continue;
            }
            if (allowed.Contains(endpoint.Key))
            {
                used.Add(endpoint.Key);
                continue;
            }
            problems.Add($"{endpoint.Key} changes data but declares the read permission {endpoint.Permission}: declare the permission for the change " +
                         $"(for example .update), mark it .ReadOnlyOperation() if it only reads, or review it in {ReviewedFile}");
        }
        problems.AddRange(allowed.Where(a => !used.Contains(a)).Select(a => $"{ReviewedFile}: '{a}' is no longer a state-changing endpoint with a read permission; remove the entry"));
        return new Result(problems, checkedCount);
    }

    public static Result Check(IEnumerable<ApiEndpoint> endpoints) => Check(endpoints, Repo.ReadReviewedList(ReviewedFile));
}
