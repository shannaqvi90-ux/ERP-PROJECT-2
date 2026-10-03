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

    public static Result Check(IEnumerable<ApiEndpoint> endpoints, IReadOnlyList<(string Entry, string Reason)> reviewed)
    {
        var problems = new List<string>();
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
