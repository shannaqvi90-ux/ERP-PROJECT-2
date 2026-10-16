using System.Globalization;
using System.Text.RegularExpressions;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// The query strings a request may carry by its endpoint's documented query parameters (critic p03
/// round 9: "Sign out everywhere ?removePasskeys=true" could skip its target check and every gate
/// stayed green, because the gates built requests from the path and the body only). Each documented
/// parameter alone with every value the gate can give it, and every parameter at once: booleans
/// true and false, every member of an enumeration, every documented example, and for an id the
/// ids the caller supplies (the target's own records and a stronger user's).
/// </summary>
public static partial class DocumentedQueries
{
    /// <summary>One query string, with the parameters it sets.</summary>
    public sealed record Variant(string Query, IReadOnlyList<(string Name, string Value)> Values)
    {
        /// <summary>Whether a value is an id (the caller's ids, which may name another user's records).</summary>
        public bool NamesIds { get; init; }

        public override string ToString() => Query;
    }

    /// <summary>The documented query parameters of an operation.</summary>
    public static IReadOnlyList<ApiParameter> Of(OpenApiDocument openApi, ApiEndpoint endpoint) =>
        openApi.Parameters(endpoint.Method, endpoint.Pattern).Where(p => p.In == "query").ToList();

    /// <summary>The values the gate sends for one parameter: <paramref name="ids"/> for an id.</summary>
    public static IReadOnlyList<string> ValuesFor(ApiParameter parameter, IReadOnlyCollection<string> ids)
    {
        var values = new List<string>();
        if (parameter.Enum is { Count: > 0 } members)
        {
            values.AddRange(members);
        }
        else if (parameter.Type == "boolean")
        {
            values.AddRange(["true", "false"]);
        }
        else if (parameter.Format == "uuid")
        {
            values.AddRange(ids);
        }
        else if (parameter.Type is "integer" or "number")
        {
            values.AddRange(["0", "1"]);
        }
        values.AddRange(parameter.Examples);
        return values.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Every variant of the endpoint's documented query parameters: each parameter alone with each
    /// of its values, then (with more than one parameter) every parameter at once, each with each
    /// of its values in turn and the others at their first value. Empty when nothing is documented.
    /// </summary>
    public static IReadOnlyList<Variant> Variants(OpenApiDocument openApi, ApiEndpoint endpoint, IReadOnlyCollection<string> ids) =>
        Variants(Of(openApi, endpoint), ids);

    public static IReadOnlyList<Variant> Variants(IReadOnlyList<ApiParameter> parameters, IReadOnlyCollection<string> ids)
    {
        var valued = parameters.Select(p => (Parameter: p, Values: ValuesFor(p, ids))).Where(p => p.Values.Count > 0).ToList();
        var variants = new List<Variant>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(IReadOnlyList<(ApiParameter Parameter, string Value)> set)
        {
            var query = "?" + string.Join("&", set.Select(s => $"{Uri.EscapeDataString(s.Parameter.Name)}={Uri.EscapeDataString(s.Value)}"));
            if (seen.Add(query))
            {
                variants.Add(new Variant(query, set.Select(s => (s.Parameter.Name, s.Value)).ToList())
                {
                    NamesIds = set.Any(s => s.Parameter.Format == "uuid" && s.Parameter.Enum is null),
                });
            }
        }
        foreach (var (parameter, values) in valued)
        {
            foreach (var value in values)
            {
                Add([(parameter, value)]);
            }
        }
        if (valued.Count > 1)
        {
            foreach (var (parameter, values) in valued)
            {
                foreach (var value in values)
                {
                    Add(valued.Select(v => v.Parameter == parameter ? (v.Parameter, value) : (v.Parameter, v.Values[0])).ToList());
                }
            }
        }
        return variants;
    }

    /// <summary>Every id (a GUID) in <paramref name="texts"/>, in the order found, without
    /// <paramref name="except"/> (the user's own id, which is the path).</summary>
    public static IReadOnlyList<string> IdsIn(IEnumerable<string> texts, params Guid[] except)
    {
        var skip = except.Select(e => e.ToString("D", CultureInfo.InvariantCulture)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return texts.SelectMany(t => GuidPattern().Matches(t).Select(m => m.Value.ToLowerInvariant()))
            .Where(id => !skip.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();
}
