using System.Text.RegularExpressions;
using Erp.Kernel.Security;

namespace Erp.Kernel.Audit;

/// <summary>
/// A kind of business record whose changes the audit trail shows (every table the capture trigger
/// records; the audit gate checks that each is registered). A module registers its own tables with
/// <c>ModuleBuilder.AuditedRecord</c>; the audit module shows their history.
/// </summary>
/// <param name="Table"><c>schema.table</c> of the records (the schema is the registering module's).</param>
/// <param name="LabelKey">Web string key of what one such record is called ("User"), in the
/// registering module's own strings.</param>
/// <param name="ReadPermission">The permission that reads such records. A record's history is shown
/// only to a reader who holds it (in the record's company, for a record of a company): the history
/// tells everything the record ever held.</param>
/// <param name="Route">The web address of one such record, with <c>{id}</c> for its id (for example
/// <c>/identity/users?open={id}</c>), so the audit log can open it; null when the record has no
/// screen of its own.</param>
public sealed partial record AuditRecordType(string Table, string LabelKey, string ReadPermission, string? Route = null)
{
    /// <summary>The schema part of <see cref="Table"/>.</summary>
    public string Schema => Table.Split('.')[0];

    /// <summary>Problems with the registration of <paramref name="module"/> owning <paramref name="schema"/>.</summary>
    public IEnumerable<string> Problems(string module, string schema)
    {
        if (!TableRegex().IsMatch(Table))
        {
            yield return $"audited record '{Table}': the table must look like 'schema.table' (lower snake case)";
        }
        else if (Schema != schema)
        {
            yield return $"audited record '{Table}': module '{module}' may register only tables of its own schema '{schema}'";
        }
        if (string.IsNullOrWhiteSpace(LabelKey))
        {
            yield return $"audited record '{Table}': needs a label key";
        }
        if (!PermissionDefinition.IsValidKey(ReadPermission))
        {
            yield return $"audited record '{Table}': '{ReadPermission}' is not a permission key";
        }
        if (Route is { } route && (!route.StartsWith('/') || !route.Contains("{id}", StringComparison.Ordinal)))
        {
            yield return $"audited record '{Table}': the route must be a web address starting with '/' that holds '{{id}}'";
        }
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}\\.[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex TableRegex();
}
