using System.Text.RegularExpressions;
using Erp.Gates.Tests.G1;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, set-based writes reviewed one by one (critic p02 rounds 12 and 13, plant P1: closing a branch
/// of company Y with one <c>ExecuteUpdate</c> run before the endpoint's permission check; such a
/// statement never reaches the kernel's SaveChanges checks). Every <c>ExecuteUpdate</c> and
/// <c>ExecuteDelete</c> in the product's source sits in a file reviewed in
/// tests/Gates/set-based-writes.txt with the exact number of uses and what they write. The
/// database refuses such a write on company tables anyway (G2CompanyWritePolicyTests); this keeps
/// the branch and workspace rules, which live only in the kernel's checks, out of reach of one too.
/// </summary>
public sealed partial class G2SetBasedWriteSourceTests
{
    public const string ReviewedFile = "tests/Gates/set-based-writes.txt";

    [GeneratedRegex(@"\bExecute(?:Update|Delete)(?:Async)?\s*(?:<[^>]*>\s*)?\(", RegexOptions.CultureInvariant)]
    private static partial Regex SetBasedWrite();

    [Fact]
    public void Only_reviewed_files_write_with_set_based_statements()
    {
        var files = TenantBypassScanner.SourceFiles().Select(f => (Path.GetRelativePath(Repo.Root, f).Replace('\\', '/'), File.ReadAllText(f))).ToList();
        var (problems, uses) = Check(files, Repo.ReadReviewedList(ReviewedFile));
        TestContext.Current.TestOutputHelper?.WriteLine($"{files.Count} files scanned, {uses} reviewed set-based writes");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
        Assert.True(files.Count >= Ratchet.Min("g1.tenantSourceFilesScanned"), $"{files.Count} files scanned");
    }

    [Fact]
    public void The_check_catches_a_set_based_write_in_an_unreviewed_file_and_a_new_one_in_a_reviewed_file()
    {
        // Critic p02 round 13, plant P1, as written in BranchEndpoints.Update.
        const string planted = "await db.Branches.Where(b => b.Id == id && b.IsActive).ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, false), cancellationToken);";
        var reviewed = new List<(string, string)> { ("src/Reviewed.cs 1", "one reviewed use") };
        var (problems, _) = Check(
        [
            ("src/Modules/Tenancy/Erp.Modules.Tenancy/Branches/BranchEndpoints.cs", planted),
            ("src/Reviewed.cs", "await db.Sessions.Where(s => s.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now));\n" +
                                "await db.Branches.Where(b => b.Id == id).ExecuteDeleteAsync();"),
            ("src/Clean.cs", "var executeUpdated = 1; // ExecuteUpdate mentioned in a comment without a call"),
        ], reviewed);
        Assert.Contains(problems, p => p.StartsWith("src/Modules/Tenancy/Erp.Modules.Tenancy/Branches/BranchEndpoints.cs:1", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("src/Reviewed.cs: 2 set-based writes where", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("src/Clean.cs", StringComparison.Ordinal));
        var (stale, _) = Check([("src/Reviewed.cs", "nothing here")], reviewed);
        Assert.Contains(stale, p => p.Contains("'src/Reviewed.cs' reviews 1", StringComparison.Ordinal));
    }

    public static (List<string> Problems, int Uses) Check(IReadOnlyList<(string Path, string Text)> files, IReadOnlyList<(string Entry, string Reason)> reviewed)
    {
        var problems = new List<string>();
        var allowed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (entry, reason) in reviewed)
        {
            var parts = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) || count < 1)
            {
                problems.Add($"{ReviewedFile}: '{entry}' must be '<path> <uses>'");
                continue;
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                problems.Add($"{ReviewedFile}: '{entry}' needs a reason after '#'");
            }
            allowed[parts[0]] = count;
        }
        var uses = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in files)
        {
            var found = SetBasedWrite().Matches(text);
            var count = allowed.GetValueOrDefault(path);
            if (allowed.ContainsKey(path))
            {
                seen.Add(path);
            }
            if (found.Count == 0 && count == 0)
            {
                continue;
            }
            var lines = string.Join(", ", found.Select(m => $"{path}:{text[..m.Index].Count(c => c == '\n') + 1}"));
            if (count == 0)
            {
                problems.Add($"{lines}: a set-based write (ExecuteUpdate/ExecuteDelete) never reaches the kernel's SaveChanges checks; write through the change tracker or review it in {ReviewedFile}");
            }
            else if (found.Count > count)
            {
                problems.Add($"{path}: {found.Count} set-based writes where {ReviewedFile} reviewed {count} ({lines}); review the new one and raise the count, or write through the change tracker");
            }
            else if (found.Count < count)
            {
                problems.Add($"{ReviewedFile}: '{path}' reviews {count} set-based writes but the file has {found.Count} ({lines}); lower the count");
            }
            else
            {
                uses += found.Count;
            }
        }
        foreach (var stale in allowed.Keys.Where(p => !seen.Contains(p)))
        {
            problems.Add($"{ReviewedFile}: '{stale}' names a file that no longer exists; remove the entry");
        }
        return (problems, uses);
    }
}
