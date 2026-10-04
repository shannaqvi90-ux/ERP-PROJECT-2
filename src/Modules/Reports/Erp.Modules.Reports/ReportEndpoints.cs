using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Reports;
using Erp.Kernel.Security;
using Erp.Modules.Reports.Contracts;
using Erp.Modules.Reports.Pdf;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Erp.Modules.Reports;

public sealed record ReportChoiceDto(string Value, string Label);

/// <param name="Lookup">For a reference parameter: the key of the list its value is chosen from.</param>
/// <param name="LookupEndpoint">That list's endpoint (search it with ?search=…; its rows' id is the value).</param>
/// <param name="LookupLabels">Row properties that name a record of that list, in order.</param>
public sealed record ReportParameterDto(string Key, string Label, string Type, bool Required, IReadOnlyList<ReportChoiceDto> Choices,
    string? Lookup, string? LookupEndpoint, IReadOnlyList<string> LookupLabels);

public sealed record ReportColumnDto(string Key, string Label, string Type, bool Total, bool Groupable);

/// <param name="Path">Where to run it: GET with the parameters, format, language, numerals, timeZone and groupBy.</param>
/// <param name="IsDocument">A record document (it prints one record's facts and lines).</param>
public sealed record ReportSummaryDto(string Key, string Module, string Title, string? Description, string Path, IReadOnlyList<ReportParameterDto> Parameters,
    IReadOnlyList<ReportColumnDto> Columns, string? DefaultGroupBy, bool IsDocument);

/// <param name="Path">Where to print it: GET with the list query (search, filter, sort), columns, groupBy, format and language.</param>
public sealed record PrintableListDto(string Key, string Title, string Path);

public sealed record ReportCatalogDto(IReadOnlyList<ReportSummaryDto> Items, IReadOnlyList<PrintableListDto> Lists);

/// <summary>
/// The report endpoints. <c>/api/reports/catalog</c> lists what the caller may run. Every
/// registered report is served at <c>/api/reports/run/{report key}</c> under the report's own
/// permission, and every printable list at <c>/api/reports/lists/{list key}</c> under the list's
/// permission. Each answers in the format asked for: the document as JSON (<c>format=json</c>, the
/// screen shows it), a PDF in English or Arabic (<c>format=pdf</c>), or the rows as CSV or XLSX.
/// Reports read only (a read-only transaction), inside the caller's tenant and company scope.
/// </summary>
internal static class ReportEndpoints
{
    public static readonly string[] Formats = ["json", "pdf", "csv", "xlsx"];
    private const int MaxColumns = 30;

    public static void Map(RouteGroupBuilder group)
    {
        var catalog = ((IEndpointRouteBuilder)group).ServiceProvider.GetRequiredService<ModuleCatalog>();

        group.MapGet("/catalog", Catalog)
            .WithName("reports.catalog.read")
            .WithSummary("The reports the caller may run (each needs the read permission of the data it prints) and the lists the caller may print, with titles, parameters and columns in the caller's language.")
            .WithReportParameters([Query("language", "The language of the titles and labels; the request's language by default.",
                new OpenApiSchema { Type = JsonSchemaType.String, Enum = Languages.All.Select(l => (JsonNode)JsonValue.Create(l)).ToList() })])
            .RequirePermission(ReportsPermissions.CatalogRead);

        foreach (var registration in catalog.Reports)
        {
            var key = registration.Definition.Key;
            group.MapGet("/run/" + key, (HttpContext http, ModuleCatalog c, ReportEngine engine, PdfReportRenderer pdf, ITenantDirectory tenants, CancellationToken ct) =>
                    RunAsync(c.FindReport(key)!, http, engine, pdf, tenants, ct))
                .WithName($"reports.run.{key}")
                .WithSummary($"Run the {key} report: as a JSON document (format=json), a PDF in English or Arabic (format=pdf), or its rows as CSV or XLSX.")
                .WithReportResponses()
                .WithReportParameters(ReportParameters(registration.Definition))
                .Surface(SurfaceKind.Export)
                .RequirePermission(registration.Definition.Permission);
        }

        foreach (var (list, _) in catalog.PrintableLists)
        {
            var key = list.Key;
            group.MapGet("/lists/" + key, (HttpContext http, ModuleCatalog c, ReportEngine engine, PdfReportRenderer pdf, ITenantDirectory tenants,
                        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json, CancellationToken ct) =>
                    PrintListAsync(c.PrintableLists.Single(p => p.List.Key == key), http, engine, pdf, tenants, json.Value.SerializerOptions, ct))
                .WithName($"reports.lists.{key}")
                .WithSummary($"Print the {key} list as it is filtered, sorted and grouped on screen: a JSON document, a PDF in English or Arabic, or the rows as CSV or XLSX.")
                .WithReportResponses()
                .WithReportParameters(ListParameters(list))
                .Surface(SurfaceKind.Export)
                .RequirePermission(list.Permission);
        }
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<ReportCatalogDto> Catalog(HttpContext http, ModuleCatalog catalog, ICurrentUser caller, WebStrings strings)
    {
        var asked = http.Request.Query["language"].ToString();
        var language = Languages.IsSupported(asked) ? asked : Languages.ForRequest(http);
        var items = catalog.Reports.Where(r => caller.Has(r.Definition.Permission)).Select(r =>
        {
            var d = r.Definition;
            return new ReportSummaryDto(d.Key, r.Module, strings.Get(d.LabelKey, language), d.DescriptionKey is null ? null : strings.Get(d.DescriptionKey, language),
                $"/api/reports/run/{d.Key}",
                d.Parameters.Select(p =>
                {
                    var lookup = p.Lookup is null ? null : catalog.FindList(p.Lookup);
                    return new ReportParameterDto(p.Key, strings.Get(p.LabelKey, language), JsonNamingPolicy.CamelCase.ConvertName(p.Type.ToString()), p.Required,
                        (p.Choices ?? []).Select(c => new ReportChoiceDto(c.Value, strings.Get(c.LabelKey, language))).ToList(),
                        p.Lookup, lookup?.Endpoint, lookup?.SearchFields ?? []);
                }).ToList(),
                d.Columns.Select(c => new ReportColumnDto(c.Key, strings.Get(c.LabelKey, language), JsonNamingPolicy.CamelCase.ConvertName(c.Type.ToString()), c.Total, c.Groupable)).ToList(),
                d.DefaultGroupBy, d.Facts is { Count: > 0 });
        }).ToList();
        var lists = catalog.PrintableLists.Where(p => caller.Has(p.List.Permission))
            .Select(p => new PrintableListDto(p.List.Key, strings.Get(p.List.LabelKey, language), $"/api/reports/lists/{p.List.Key}"))
            .ToList();
        return TypedResults.Ok(new ReportCatalogDto(items, lists));
    }

    private sealed record Common(string Format, ReportOptions Options, string? GroupBy, bool Inline);

    private static async Task<IResult> RunAsync(ReportRegistration registration, HttpContext http, ReportEngine engine, PdfReportRenderer pdf, ITenantDirectory tenants, CancellationToken cancellationToken)
    {
        var definition = registration.Definition;
        var validator = new Validator(http);
        var common = await CommonAsync(http, validator, definition.Columns.Where(c => c.Groupable).Select(c => c.Key).ToList(), definition.DefaultGroupBy, tenants, cancellationToken);
        var parameters = ReadParameters(definition, http.Request.Query, validator);
        if (!validator.IsValid || common is null)
        {
            return validator.ToResult();
        }
        var limit = common.Format is "csv" or "xlsx" ? ReportEngine.ExportRowLimit : ReportEngine.DocumentRowLimit;
        var run = new ReportRun(definition, parameters, common.Options.Language, limit);
        var source = (IReportSource)http.RequestServices.GetRequiredService(registration.SourceType);
        var data = await source.RunAsync(run, cancellationToken);
        if (data is null)
        {
            return Problems.NotFound(http);
        }
        var document = await engine.BuildAsync(definition, data, run, common.GroupBy, common.Options, cancellationToken);
        return Output(http, document, common, pdf);
    }

    private static async Task<IResult> PrintListAsync((ListDefinition List, ListRowReader Reader) printable, HttpContext http, ReportEngine engine, PdfReportRenderer pdf,
        ITenantDirectory tenants, JsonSerializerOptions json, CancellationToken cancellationToken)
    {
        var (list, reader) = printable;
        var validator = new Validator(http);
        var query = http.Request.Query;
        var common = await CommonAsync(http, validator, list.Columns.Where(c => c.Groupable).Select(c => c.Key).ToList(), null, tenants, cancellationToken);
        var columns = list.Columns.Where(c => !c.Hidden).ToList();
        if (Single(query, "columns") is { } names)
        {
            var keys = names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var chosen = keys.Select(list.Column).ToList();
            if (keys.Length == 0 || keys.Length > MaxColumns || chosen.Any(c => c is null) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            {
                validator.Add("columns", "reportColumns");
            }
            else
            {
                columns = chosen!;
            }
        }
        if (!validator.IsValid || common is null)
        {
            return validator.ToResult();
        }
        var limit = common.Format is "csv" or "xlsx" ? ReportEngine.ExportRowLimit : ReportEngine.DocumentRowLimit;
        var rows = new List<JsonElement>();
        string? after = null;
        var total = 0;
        while (true)
        {
            var request = new ListRequest
            {
                Search = Single(query, "search"),
                Filter = Single(query, "filter"),
                Sort = Single(query, "sort"),
                After = after,
                Take = Math.Min(ListRequest.MaxTake, limit - rows.Count),
            };
            var page = await reader(http.RequestServices, request, http, cancellationToken);
            if (page.Problem is { } problem)
            {
                return problem;
            }
            total = page.Total;
            rows.AddRange(page.Rows.Select(r => JsonSerializer.SerializeToElement(r, r.GetType(), json)));
            if (page.Next is null || rows.Count >= limit)
            {
                break;
            }
            after = page.Next;
        }
        var shown = new ListRequest { Search = Single(query, "search"), Filter = Single(query, "filter"), Sort = Single(query, "sort") };
        var document = await engine.BuildListAsync(list, rows, Math.Max(total, rows.Count), shown, columns, common.GroupBy, common.Options, cancellationToken);
        return Output(http, document, common, pdf);
    }

    private static IResult Output(HttpContext http, ReportDocument document, Common common, PdfReportRenderer pdf)
    {
        // The moment printed, declared so a client (and the isolation gate) can tell the stamp
        // from the content: the instant exactly as the JSON carries it, then the printed text.
        var instant = JsonSerializer.Serialize(document.PrintedAt).Trim('"');
        http.Response.Headers["X-Erp-Printed-At"] = $"{instant};{Uri.EscapeDataString(document.PrintedAtText)}";
        var name = FileName(document);
        switch (common.Format)
        {
            case "pdf":
                var bytes = pdf.Render(document);
                if (common.Inline)
                {
                    http.Response.Headers.ContentDisposition = $"inline; filename=\"{AsciiName(name)}.pdf\"; filename*=UTF-8''{Uri.EscapeDataString(name)}.pdf";
                    return Results.File(bytes, "application/pdf");
                }
                return Results.File(bytes, "application/pdf", name + ".pdf");
            case "csv":
                return Results.File(Exports.Csv(document), Exports.CsvType + "; charset=utf-8", name + ".csv");
            case "xlsx":
                return Results.File(Exports.Xlsx(document), Exports.XlsxType, name + ".xlsx");
            default:
                return TypedResults.Ok(document);
        }
    }

    /// <summary>The download's name: the title in the document's language and the print date.</summary>
    private static string FileName(ReportDocument document)
    {
        var title = new string(document.Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c is not ('"' or '\'' or ';')).ToArray()).Trim();
        return $"{(title.Length == 0 ? "report" : title)} {document.PrintedAt.UtcDateTime:yyyy-MM-dd}";
    }

    private static string AsciiName(string name)
    {
        var ascii = new string(name.Select(c => c is >= ' ' and < '\u007f' ? c : '_').ToArray());
        return ascii.Replace("\\", "_", StringComparison.Ordinal);
    }

    private static async Task<Common?> CommonAsync(HttpContext http, Validator validator, IReadOnlyList<string> groupable, string? defaultGroupBy,
        ITenantDirectory tenants, CancellationToken cancellationToken)
    {
        var query = http.Request.Query;
        var format = Single(query, "format") ?? "json";
        if (!Formats.Contains(format))
        {
            validator.Add("format", "reportFormat", string.Join(", ", Formats));
        }
        var language = Single(query, "language") ?? Languages.ForRequest(http);
        validator.OneOf("language", language, Languages.All.ToList());
        var numerals = Single(query, "numerals") ?? NumeralSystems.Latin;
        validator.OneOf("numerals", numerals, NumeralSystems.All.ToList());
        var zoneName = Single(query, "timeZone");
        TimeZoneInfo? zone = null;
        if (zoneName is not null)
        {
            if (zoneName.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(zoneName, out zone))
            {
                validator.Add("timeZone", "reportTimeZone");
            }
        }
        var groupBy = query.ContainsKey("groupBy") ? Single(query, "groupBy") : defaultGroupBy;
        if (groupBy is not null && !groupable.Contains(groupBy))
        {
            validator.Add("groupBy", "reportGroupBy", groupable.Count == 0 ? "\u2014" : string.Join(", ", groupable));
        }
        var disposition = Single(query, "disposition") ?? "attachment";
        validator.OneOf("disposition", disposition, ["attachment", "inline"]);
        if (!validator.IsValid)
        {
            return null;
        }
        if (zone is null)
        {
            var tenant = await tenants.GetCurrentAsync(cancellationToken);
            zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant?.TimeZone ?? "Asia/Dubai", out var found) ? found : TimeZoneInfo.Utc;
        }
        return new Common(format, new ReportOptions(language, numerals, zone), groupBy, disposition == "inline");
    }

    /// <summary>The report's parameters from the query string, typed and checked.</summary>
    private static Dictionary<string, object> ReadParameters(ReportDefinition definition, IQueryCollection query, Validator validator)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters)
        {
            var raw = Single(query, parameter.Key);
            if (raw is null)
            {
                if (parameter.Required)
                {
                    validator.Add(parameter.Key, "required");
                }
                continue;
            }
            switch (parameter.Type)
            {
                case ReportParameterType.Text:
                    if (raw.Length > ListRequest.MaxSearchLength)
                    {
                        validator.Add(parameter.Key, "maxLength", ListRequest.MaxSearchLength);
                    }
                    else
                    {
                        values[parameter.Key] = raw;
                    }
                    break;
                case ReportParameterType.Date:
                    if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    {
                        values[parameter.Key] = date;
                    }
                    else
                    {
                        validator.Add(parameter.Key, "reportDate");
                    }
                    break;
                case ReportParameterType.Boolean:
                    if (raw is "true" or "false")
                    {
                        values[parameter.Key] = raw == "true";
                    }
                    else
                    {
                        validator.Add(parameter.Key, "reportBoolean");
                    }
                    break;
                case ReportParameterType.Choice:
                    if (parameter.Choices!.Any(c => c.Value == raw))
                    {
                        values[parameter.Key] = raw;
                    }
                    else
                    {
                        validator.Add(parameter.Key, "oneOf", string.Join(", ", parameter.Choices!.Select(c => c.Value)));
                    }
                    break;
                case ReportParameterType.Reference:
                    if (Guid.TryParse(raw, out var id))
                    {
                        values[parameter.Key] = id;
                    }
                    else
                    {
                        validator.Add(parameter.Key, "reportId");
                    }
                    break;
            }
        }
        return values;
    }

    /// <summary>A query value, or null when absent or blank (the last one when repeated).</summary>
    private static string? Single(IQueryCollection query, string name) =>
        query.TryGetValue(name, out var values) && values.Count > 0 && !string.IsNullOrWhiteSpace(values[^1]) ? values[^1]!.Trim() : null;

    // OpenAPI: the query parameters and the content types of every report route.

    private static IReadOnlyList<OpenApiParameter> CommonParameters(IReadOnlyList<string> groupable, string? defaultGroupBy)
    {
        static JsonNode Text(string v) => JsonValue.Create(v);
        return
        [
            Query("format", "json (the document), pdf, csv or xlsx; json by default.", new OpenApiSchema { Type = JsonSchemaType.String, Enum = Formats.Select(Text).ToList() }),
            Query("language", "The document's language, which may differ from the screen's; the request's language by default.",
                new OpenApiSchema { Type = JsonSchemaType.String, Enum = Languages.All.Select(Text).ToList() }),
            Query("numerals", "Digits of an Arabic document: latn (0-9) or arab (\u0660-\u0669); latn by default.",
                new OpenApiSchema { Type = JsonSchemaType.String, Enum = NumeralSystems.All.Select(Text).ToList() }),
            Query("timeZone", "IANA time zone the document shows times in; the workspace's by default.",
                new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 64, Examples = [Text("Asia/Dubai")] }),
            Query("groupBy", defaultGroupBy is null ? "A column to group the rows by (with counts and totals)." : $"A column to group the rows by; {defaultGroupBy} by default.",
                groupable.Count > 0 ? new OpenApiSchema { Type = JsonSchemaType.String, Enum = groupable.Select(Text).ToList() } : new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 0 }),
            Query("disposition", "attachment (download, the default) or inline (open in the browser), for a PDF.",
                new OpenApiSchema { Type = JsonSchemaType.String, Enum = [Text("attachment"), Text("inline")] }),
        ];
    }

    private static IReadOnlyList<OpenApiParameter> ReportParameters(ReportDefinition definition)
    {
        static JsonNode Text(string v) => JsonValue.Create(v);
        var parameters = definition.Parameters.Select(p => Query(p.Key, $"{p.Type} parameter{(p.Required ? " (required)" : "")}.", p.Type switch
        {
            ReportParameterType.Date => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
            ReportParameterType.Boolean => new OpenApiSchema { Type = JsonSchemaType.Boolean },
            ReportParameterType.Choice => new OpenApiSchema { Type = JsonSchemaType.String, Enum = p.Choices!.Select(c => Text(c.Value)).ToList() },
            ReportParameterType.Reference => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            _ => new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = ListRequest.MaxSearchLength },
        }, p.Required)).ToList();
        return [.. parameters, .. CommonParameters(definition.Columns.Where(c => c.Groupable).Select(c => c.Key).ToList(), definition.DefaultGroupBy)];
    }

    private static IReadOnlyList<OpenApiParameter> ListParameters(ListDefinition list)
    {
        static JsonNode Text(string v) => JsonValue.Create(v);
        var (sort, filter, _) = ViewExampleFor(list);
        return
        [
            Query("search", "Words to find (the list's search).", new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = ListRequest.MaxSearchLength }),
            Query("filter", "Filter in the list filter language.", new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = ListFilter.MaxLength, Examples = filter is null ? null : [Text(filter)] }),
            Query("sort", "Sort as in the list's sort parameter.", new OpenApiSchema { Type = JsonSchemaType.String, Examples = sort is null ? null : [Text(sort)] }),
            Query("columns", "Column keys to print, separated by commas; the list's visible columns by default.",
                new OpenApiSchema { Type = JsonSchemaType.String, Examples = [Text(string.Join(",", list.Columns.Where(c => !c.Hidden).Select(c => c.Key)))] }),
            .. CommonParameters(list.Columns.Where(c => c.Groupable).Select(c => c.Key).ToList(), null),
        ];
    }

    /// <summary>A valid sort and filter of the list for the API document's examples.</summary>
    private static (string? Sort, string? Filter, string? GroupBy) ViewExampleFor(ListDefinition list)
    {
        var sort = list.DefaultSort ?? list.Columns.FirstOrDefault(c => c.Sortable)?.Key;
        var filter = (list.Presets ?? []).Select(p => p.Filter).FirstOrDefault(f => f is not null);
        return (sort, filter, list.Columns.FirstOrDefault(c => c.Groupable)?.Key);
    }

    private static OpenApiParameter Query(string name, string description, OpenApiSchema schema, bool required = false) =>
        new() { Name = name, In = ParameterLocation.Query, Description = description, Required = required, Schema = schema };

    private static RouteHandlerBuilder WithReportParameters(this RouteHandlerBuilder builder, IReadOnlyList<OpenApiParameter> parameters) =>
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            operation.Parameters ??= [];
            foreach (var parameter in parameters)
            {
                operation.Parameters.Add(parameter);
            }
            return Task.CompletedTask;
        });

    private static RouteHandlerBuilder WithReportResponses(this RouteHandlerBuilder builder) =>
        builder.Produces<ReportDocument>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf", additionalContentTypes: [Exports.CsvType, Exports.XlsxType])
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}
