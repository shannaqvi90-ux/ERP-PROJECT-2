using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Gates.Tests.G1;
using Erp.Gates.Tests.Infrastructure;
using Erp.Testing;

namespace Erp.Gates.Tests.G2;

/// <summary>
/// G2, a role held in company X acts on nothing of company Y, one field at a time (critic p02
/// rounds 12 and 13, plant P1: a branch of Y closed by a manager of X alone, through a write that
/// ran only when isActive went from true to false, before the endpoint's per-company check). A
/// gate that sends Y's records only freshly generated bodies (every boolean true) or edits a single
/// text field never reaches a path that runs only when one particular field changes. So for every
/// endpoint that edits a record of Y (its own GET answers the record as JSON to the tenant's
/// administrator), and for every endpoint that creates one in Y (its body names Y's id), the
/// attacker sends one request per writable field: the record exactly as read with that one field
/// changed to another valid value (booleans flipped, an enum's other value, another code, another
/// country, phone, currency, month or day), and one with that field left out. Y's record must not
/// change after any of them; nothing may be added to Y.
///
/// Each variant is also sent at a record of company X (the control): accepted there, the variant
/// is a valid edit and Y's must answer 403. A variant the attacker cannot make in X either is
/// sent by the tenant's administrator: refused for them as well, it is a change nobody may make
/// (a branch moved to another company) and Y's must be refused too; accepted for them, the gate
/// cannot tell a per-company check from a malformed request, which is reported. Every accepted
/// change in X is put back by the administrator straight away. A field the gate has no other
/// valid value for is reported as a blind spot: the gate is extended, the field is not skipped.
/// </summary>
public static class CompanyRoleVariants
{
    public sealed record Result(IReadOnlyList<string> Problems, IReadOnlyList<string> Sent, IReadOnlyList<string> Endpoints);

    public delegate Task<(int Status, string Text)> Send(HttpClient client, string method, string path, JsonNode? body);

    /// <summary>At most this many of Y's records per endpoint (its company row first, then the rows of
    /// each company table in id order): every field of every endpoint is varied on each.</summary>
    public const int RecordsPerEndpoint = 2;

    public static async Task<Result> RunAsync(ErpTestEnvironment env, OpenApiDocument openApi, CompanyRoleRecords.Setup setup,
        IReadOnlyList<ApiEndpoint> endpoints, IReadOnlyList<string> yIds, IReadOnlyList<string> xIds, Send send)
    {
        var problems = new List<string>();
        var sent = new List<string>();
        var covered = new List<string>();
        var n = 0;
        foreach (var endpoint in endpoints.Where(e => e.HasBody))
        {
            if (openApi.RequestSchema(endpoint.Method, endpoint.Pattern) is not { } schema)
            {
                continue;
            }
            if (endpoint.RouteParameters.Count == 0)
            {
                // A create naming Y's id in a company field (a branch opened in Y).
                if (endpoint.Method != "POST" || !G1WriteOracle.Valid(openApi, schema, env, "probe", setup.Y.ToString()).ToJsonString().Contains(setup.Y.ToString(), StringComparison.Ordinal))
                {
                    continue;
                }
                covered.Add(endpoint.Key);
                foreach (var spec in FieldVariants.Specs(openApi, schema).Where(s => !IsCompanyField(s.Field)))
                {
                    var tag = $"c{++n}";
                    var yBody = Variant(openApi, schema, spec, G1WriteOracle.Valid(openApi, schema, env, $"{tag}y", setup.Y.ToString()), tag, env, setup);
                    var xBody = Variant(openApi, schema, spec, G1WriteOracle.Valid(openApi, schema, env, $"{tag}x", setup.X.ToString()), tag, env, setup);
                    if (yBody is null || xBody is null)
                    {
                        problems.Add($"{endpoint} {spec}: the gate has no different valid value for this field; extend CompanyRoleVariants or FieldVariants rather than leave it untested");
                        continue;
                    }
                    var (yStatus, yText) = await send(setup.Attacker, endpoint.Method, endpoint.Pattern, yBody);
                    var (xStatus, xText) = await send(setup.Attacker, endpoint.Method, endpoint.Pattern, xBody);
                    await JudgeAsync(problems, $"{endpoint} {spec}", spec, yStatus, yText, xStatus, xText,
                        async () => (await send(setup.Admin, endpoint.Method, endpoint.Pattern, Variant(openApi, schema, spec, G1WriteOracle.Valid(openApi, schema, env, $"{tag}a", setup.X.ToString()), $"{tag}a", env, setup))).Status);
                    sent.Add($"{endpoint} {spec}");
                }
                continue;
            }
            if (endpoint.RouteParameters.Count != 1 || endpoint.Method is not ("PUT" or "PATCH"))
            {
                continue;
            }
            var yTargets = new List<string>();
            foreach (var id in yIds)
            {
                if (yTargets.Count < RecordsPerEndpoint && await RecordAsync(setup.Admin, endpoint.Path(_ => id)) is not null)
                {
                    yTargets.Add(id);
                }
            }
            if (yTargets.Count == 0)
            {
                continue;
            }
            string? xTarget = null;
            foreach (var id in xIds)
            {
                if (await RecordAsync(setup.Admin, endpoint.Path(_ => id)) is not null)
                {
                    xTarget = id;
                    break;
                }
            }
            if (xTarget is null)
            {
                problems.Add($"{endpoint}: edits company Y's records but no record of company X answers its GET, so no variant can be controlled");
                continue;
            }
            covered.Add(endpoint.Key);
            var xPath = endpoint.Path(_ => xTarget);
            foreach (var yTarget in yTargets)
            {
                var yPath = endpoint.Path(_ => yTarget);
                foreach (var spec in FieldVariants.Specs(openApi, schema))
                {
                    var tag = $"e{++n}";
                    var yRecord = await RecordAsync(setup.Admin, yPath);
                    var xRecord = await RecordAsync(setup.Admin, xPath);
                    if (yRecord is null || xRecord is null)
                    {
                        problems.Add($"{endpoint} {spec}: the record of {(yRecord is null ? $"company Y ({yPath})" : $"company X ({xPath})")} no longer answers its GET");
                        continue;
                    }
                    var yBody = Variant(openApi, schema, spec, Edit(openApi, schema, env, yRecord, tag, setup.Y), tag, env, setup);
                    var xBody = Variant(openApi, schema, spec, Edit(openApi, schema, env, xRecord, tag, setup.X), tag, env, setup);
                    if (yBody is null || xBody is null)
                    {
                        problems.Add($"{endpoint} {spec}: the gate has no different valid value for this field; extend CompanyRoleVariants or FieldVariants rather than leave it untested");
                        continue;
                    }
                    var (yStatus, yText) = await send(setup.Attacker, endpoint.Method, yPath, yBody);
                    var yAfter = await RecordAsync(setup.Admin, yPath);
                    if (yAfter is null || !JsonNode.DeepEquals(yAfter, yRecord))
                    {
                        problems.Add($"{endpoint} {spec}: company Y's record {yTarget} changed (answered {yStatus}): before {Short(yRecord.ToJsonString())}; after {Short(yAfter?.ToJsonString() ?? "gone")}");
                    }
                    var (xStatus, xText) = await send(setup.Attacker, endpoint.Method, xPath, xBody);
                    if (xStatus is >= 200 and < 300)
                    {
                        await RestoreAsync(problems, setup.Admin, openApi, schema, env, endpoint, xPath, xRecord, tag, setup.X, send);
                    }
                    await JudgeAsync(problems, $"{endpoint} {spec}", spec, yStatus, yText, xStatus, xText, async () =>
                    {
                        var current = await RecordAsync(setup.Admin, xPath) ?? xRecord;
                        var adminBody = Variant(openApi, schema, spec, Edit(openApi, schema, env, current, $"{tag}a", setup.X), $"{tag}a", env, setup);
                        var (status, _) = await send(setup.Admin, endpoint.Method, xPath, adminBody);
                        if (status is >= 200 and < 300)
                        {
                            await RestoreAsync(problems, setup.Admin, openApi, schema, env, endpoint, xPath, xRecord, tag, setup.X, send);
                        }
                        return status;
                    });
                    sent.Add($"{endpoint} {spec}");
                }
            }
        }
        return new Result(problems, sent, covered);
    }

    private static async Task JudgeAsync(List<string> problems, string what, FieldVariants.Spec spec, int yStatus, string yText, int xStatus, string xText, Func<Task<int>> adminOnX)
    {
        var accepted = xStatus is >= 200 and < 300;
        if (accepted)
        {
            if (yStatus != 403)
            {
                problems.Add($"{what}: accepted in company X ({xStatus}) but answered {yStatus} for company Y, where the role is not held (expected 403): {Short(yText)}");
            }
            return;
        }
        if (yStatus is >= 200 and < 300)
        {
            problems.Add($"{what}: refused in company X ({xStatus}) yet accepted for company Y ({yStatus}): {Short(yText)}");
            return;
        }
        if (spec.Kind != FieldVariants.Kind.Changed)
        {
            return;
        }
        // A change the attacker cannot make in X: valid for nobody, or the gate cannot tell.
        var admin = await adminOnX();
        if (admin is >= 200 and < 300)
        {
            problems.Add($"{what}: refused for the manager of X in company X ({xStatus}) but accepted for the tenant's administrator, so the gate cannot tell a per-company check from a malformed request: {Short(xText)}");
        }
    }

    /// <summary>The record as its own GET answers it to <paramref name="reader"/>, or null when it
    /// does not answer a JSON object (a logo's bytes, a user's id where a branch's is expected).</summary>
    public static async Task<JsonObject?> RecordAsync(HttpClient reader, string path)
    {
        using var response = await reader.GetAsync(path);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not { } type || !type.Contains("json", StringComparison.Ordinal))
        {
            return null;
        }
        return JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject;
    }

    /// <summary>An edit that changes nothing: a valid body whose every field the record answers is
    /// the record's own value.</summary>
    private static JsonObject Edit(OpenApiDocument openApi, JsonElement schema, ErpTestEnvironment env, JsonObject record, string tag, Guid company)
    {
        var body = G1WriteOracle.Valid(openApi, schema, env, tag, company.ToString());
        foreach (var (field, _) in body.ToList())
        {
            if (record.TryGetPropertyValue(field, out var current))
            {
                body[field] = current?.DeepClone();
            }
        }
        return body;
    }

    private static async Task RestoreAsync(List<string> problems, HttpClient admin, OpenApiDocument openApi, JsonElement schema, ErpTestEnvironment env,
        ApiEndpoint endpoint, string path, JsonObject original, string tag, Guid company, Send send)
    {
        var current = await RecordAsync(admin, path);
        var body = Edit(openApi, schema, env, original, $"{tag}r", company);
        if (current?["version"] is { } version && body.ContainsKey("version"))
        {
            body["version"] = version.DeepClone();
        }
        var (status, text) = await send(admin, endpoint.Method, path, body);
        if (status is < 200 or >= 300)
        {
            problems.Add($"{endpoint}: putting company X's record {path} back after a variant answered {status}: {Short(text)}");
        }
    }

    private static bool IsCompanyField(string field) => field.Equals("companyId", StringComparison.OrdinalIgnoreCase);

    /// <summary>The body with <paramref name="spec"/>'s field changed or left out; fields
    /// <see cref="FieldVariants"/> has no value for get one here.</summary>
    private static JsonObject? Variant(OpenApiDocument openApi, JsonElement schema, FieldVariants.Spec spec, JsonObject baseBody, string tag, ErpTestEnvironment env, CompanyRoleRecords.Setup setup)
    {
        if (spec.Kind == FieldVariants.Kind.Changed && Other(spec.Field, baseBody, tag) is { } other)
        {
            var body = (JsonObject)baseBody.DeepClone();
            body[spec.Field] = other;
            return body;
        }
        return FieldVariants.Apply(openApi, schema, spec, baseBody, tag, env.TenantA.EmailDomain, (_, _) => null,
            (field, current) => IsCompanyField(field)
                ? JsonValue.Create((current?.ToString() == setup.X.ToString() ? setup.Y : setup.X).ToString())
                : null);
    }

    /// <summary>Another valid value of the company and branch fields the generic variants cannot
    /// vary (integers, codes that must stay unique, patterned values whose example is the current one).</summary>
    private static JsonNode? Other(string field, JsonObject body, string tag)
    {
        var current = body[field] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : body[field]?.ToJsonString();
        string Pick(string a, string b) => current == a ? b : a;
        return field switch
        {
            "code" => JsonValue.Create($"V{Convert.ToHexString(Encoding.UTF8.GetBytes(tag)).ToUpperInvariant()}"[..Math.Min(20, 1 + tag.Length * 2)]),
            // An emirate is only for the UAE: with one set, the same country in the other letter case.
            "country" => JsonValue.Create(body["emirate"] is JsonValue emirate && emirate.GetValueKind() != JsonValueKind.Null ? (current ?? "AE").ToLowerInvariant() : Pick("AE", "OM")),
            "phone" => JsonValue.Create(Pick("+971 4 765 4321", "+971 4 123 4567")),
            "poBox" => JsonValue.Create(Pick("54321", "12345")),
            "taxRegistrationNumber" => JsonValue.Create(Pick("100987654300003", "100123456700003")),
            "baseCurrency" => JsonValue.Create(Pick("USD", "AED")),
            "website" => JsonValue.Create(Pick("https://varied.example.ae", "https://www.example.ae")),
            "fiscalYearStartMonth" => JsonValue.Create(current == "4" ? 1 : 4),
            "fiscalYearStartDay" => JsonValue.Create(current == "15" ? 1 : 15),
            _ => null,
        };
    }

    private static string Short(string text) => text.Length <= 240 ? text : text[..240] + "…";
}
