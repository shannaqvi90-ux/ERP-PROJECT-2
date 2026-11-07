using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.CustomFields.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Validator = Erp.Kernel.Http.Validator;

namespace Erp.Modules.CustomFields;

/// <summary>One option of a single-select field. <c>value</c> is what records store (letters and
/// digits, lower camel case); left out, it is made from the English label.</summary>
public sealed record FieldOptionRequest(
    [property: ApiExample("gold"), StringLength(FieldRules.OptionValueMaxLength)] string? Value,
    [property: ApiExample("Gold"), StringLength(FieldRules.LabelMaxLength)] string? LabelEn,
    [property: ApiExample("ذهبي"), StringLength(FieldRules.LabelMaxLength)] string? LabelAr);

/// <summary>A custom field as its form shows it.</summary>
public sealed record FieldDto(
    Guid Id,
    string RecordType,
    string Key,
    string LabelEn,
    string LabelAr,
    string Type,
    bool Required,
    string? Default,
    int Decimals,
    IReadOnlyList<CustomFieldOption> Options,
    string? LookupTarget,
    int SortOrder,
    bool Searchable,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version,
    [property: Description("True when the caller works in every company of the workspace and every branch of each: only then may they change custom fields, which every company shares.")]
    bool EveryCompany);

/// <summary>A row of the custom fields list.</summary>
public sealed record FieldRow(
    Guid Id,
    string RecordType,
    string Key,
    string LabelEn,
    string LabelAr,
    string FieldType,
    bool Required,
    bool Searchable,
    int SortOrder,
    bool IsActive,
    DateTimeOffset UpdatedAt,
    uint Version);

/// <summary>A kind of record that can have custom fields, and how many it has.</summary>
public sealed record RecordTypeDto(
    string Table,
    [property: Description("Web string key of what the records are called.")] string LabelKey,
    [property: Description("True when a lookup field can point at this kind of record.")] bool CanBePointedAt,
    int FieldCount);

/// <summary>
/// Add or change a custom field. <c>recordType</c> and <c>type</c> are chosen when the field is
/// added and never change. Labels in English and Arabic (the English one is required; a blank Arabic label shows the English one). <c>default</c> is the value
/// a new record starts with, written as a value of the field's kind (a number with a dot, a date as
/// yyyy-mm-dd, true or false, an option's value); a lookup has none. <c>decimals</c> (0 to 8) is for
/// a number. <c>options</c> (1 to 50) is for a single select. <c>lookupTarget</c> is for a lookup.
/// <c>searchable</c> (text fields) lets the list's search box match the value. <c>isActive</c> false
/// switches the field off everywhere without losing its values. On change, <c>version</c> is the
/// version that was read. Properties that do not belong to the field's kind (decimals of a text field,
/// options of a number) are ignored.
/// </summary>
public sealed record SaveFieldRequest(
    [property: ApiExample("contacts.contacts")] string? RecordType,
    [property: ApiExample("Customer segment"), StringLength(FieldRules.LabelMaxLength)] string? LabelEn,
    [property: ApiExample("شريحة العميل"), StringLength(FieldRules.LabelMaxLength)] string? LabelAr,
    [property: ApiExample("text")] string? Type,
    bool? Required,
    [property: ApiExample("Dubai"), StringLength(CustomFieldsDbContext.MaxTextLength)] string? Default,
    [property: Range(0, 8)] int? Decimals,
    IReadOnlyList<FieldOptionRequest>? Options,
    [property: ApiExample("contacts.contacts")] string? LookupTarget,
    [property: Range(0, 10000)] int? SortOrder,
    bool? Searchable,
    bool? IsActive,
    uint? Version);

internal static class FieldEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/fields", List)
            .WithName("customfields.fields.list")
            .WithSummary("The workspace's custom field definitions, a page at a time: word search on labels and keys, filters, sort, paging and grouping (the list query contract); by kind of record and display order by default.")
            .RequirePermission(CustomFieldsPermissions.FieldsRead);

        group.MapGet("/fields/{id:guid}", Get)
            .WithName("customfields.fields.get")
            .WithSummary("One custom field definition.")
            .RequirePermission(CustomFieldsPermissions.FieldsRead);

        group.MapPost("/fields", Create)
            .WithName("customfields.fields.create")
            .WithSummary("Add a custom field to a kind of record (needs every company of the workspace, which shares the fields). It appears at once in the record's form, list (column chooser, filters and, for text, search), API and audit trail. The machine name is made from the English label.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(CustomFieldsPermissions.FieldsManage);

        group.MapPut("/fields/{id:guid}", Update)
            .WithName("customfields.fields.update")
            .WithSummary("Change a custom field: labels, required flag, default, decimals, options, order, search, or switch it off or on. Its kind of record and its type never change; a field is switched off, never deleted, so records keep their values.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(CustomFieldsPermissions.FieldsManage);

        group.MapGet("/record-types", RecordTypes)
            .WithName("customfields.recordTypes.list")
            .WithSummary("The kinds of record that can have custom fields, with how many each has.")
            .RequirePermission(CustomFieldsPermissions.FieldsRead);
    }

    private static async Task<Results<Ok<ListPage<FieldRow>>, ProblemHttpResult>> List(
        CustomFieldsDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the custom fields list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<FieldRow>> PageAsync(CustomFieldsDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var rows = (await db.Definitions.AsNoTracking().ToListAsync(cancellationToken))
            .Select(d => new FieldRow(d.Id, d.RecordType, d.Key, d.LabelEn, d.LabelAr, d.FieldType, d.Required, d.Searchable, d.SortOrder, d.IsActive, d.UpdatedAt, d.Version))
            .ToList();
        return await catalog.ListBinding<FieldRow>(FieldsList.Key).QueryAsync(rows.AsQueryable(), request, http, cancellationToken);
    }

    private static async Task<Results<Ok<FieldDto>, ProblemHttpResult>> Get(Guid id, CustomFieldsDbContext db, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var field = await db.Definitions.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return field is null ? Problems.NotFound(http) : TypedResults.Ok(ToDto(field, session));
    }

    private static async Task<Ok<IReadOnlyList<RecordTypeDto>>> RecordTypes(CustomFieldsDbContext db, ModuleCatalog catalog, CancellationToken cancellationToken)
    {
        var counts = await db.Definitions.AsNoTracking().GroupBy(d => d.RecordType).Select(g => new { Type = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Type, g => g.Count, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<RecordTypeDto>>(catalog.CustomFieldRecordTypes
            .Select(t => new RecordTypeDto(t.Table, t.LabelKey, t.Names is not null, counts.GetValueOrDefault(t.Table))).ToList());
    }

    private static async Task<Results<Created<FieldDto>, ProblemHttpResult>> Create(
        SaveFieldRequest request, CustomFieldsDbContext db, ModuleCatalog catalog, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http).Required("recordType", request.RecordType).Required("type", request.Type).Required("labelEn", request.LabelEn);
        var recordType = request.RecordType?.Trim();
        var type = request.Type?.Trim();
        validator.Must(recordType is null || catalog.CustomFieldRecordTypes.Any(t => t.Table == recordType), "recordType", "customFieldRecordType");
        validator.Must(type is null || CustomFieldTypes.All.Contains(type), "type", "customFieldType");
        // A text field is searched by the list's search box unless it says otherwise.
        var field = new FieldDefinition { RecordType = recordType ?? "", FieldType = type ?? CustomFieldTypes.Text, Searchable = true };
        Check(validator, catalog, field, request);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!session.HoldsWholeWorkspace)
        {
            return Problems.Forbidden(http, "customfields.workspaceNeedsEveryCompany");
        }
        // A kind of record shows at most MaxFieldsPerRecordType fields (forms, list columns, filters). A field added
        // beyond that is saved switched off, with its definition kept, until the administrator switches another off;
        // refusing it would leave the administrator's add with nothing saved, and every attack of the isolation
        // gates adds one.
        if (field.IsActive && await db.Definitions.CountAsync(d => d.RecordType == field.RecordType && d.IsActive, cancellationToken) >= CustomFieldsDbContext.MaxFieldsPerRecordType)
        {
            field.IsActive = false;
        }
        var taken = await db.Definitions.AsNoTracking().Where(d => d.RecordType == field.RecordType).Select(d => d.Key).ToListAsync(cancellationToken);
        var baseKey = FieldRules.KeyFor(field.LabelEn);
        var key = baseKey;
        // A name that is taken gets six random letters and digits, not the next number: the next number is the same in
        // every workspace, so two workspaces' fields would carry the same machine names.
        while (taken.Contains(key, StringComparer.Ordinal))
        {
            key = baseKey[..Math.Min(baseKey.Length, 34)] + System.Security.Cryptography.RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", 6);
        }
        field.Key = key;
        field.SortOrder = request.SortOrder ?? (taken.Count == 0 ? 10 : (await db.Definitions.Where(d => d.RecordType == field.RecordType).MaxAsync(d => d.SortOrder, cancellationToken)) + 10);
        db.Definitions.Add(field);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/customfields/fields/{field.Id}", ToDto(field, session));
    }

    private static async Task<Results<Ok<FieldDto>, ProblemHttpResult>> Update(
        Guid id, SaveFieldRequest request, CustomFieldsDbContext db, ModuleCatalog catalog, ErpDbSession session, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http).Required("labelEn", request.LabelEn).Required("version", request.Version);
        var field = await db.Definitions.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (field is null)
        {
            return validator.IsValid ? Problems.NotFound(http) : validator.ToResult();
        }
        var wasActive = field.IsActive;
        validator.Must(request.RecordType is null || request.RecordType.Trim() == field.RecordType, "recordType", "customFieldFixed");
        validator.Must(request.Type is null || request.Type.Trim() == field.FieldType, "type", "customFieldFixed");
        Check(validator, catalog, field, request);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!session.HoldsWholeWorkspace)
        {
            return Problems.Forbidden(http, "customfields.workspaceNeedsEveryCompany");
        }
        if (field.IsActive && !wasActive && await db.Definitions.CountAsync(d => d.RecordType == field.RecordType && d.IsActive && d.Id != field.Id, cancellationToken) >= CustomFieldsDbContext.MaxFieldsPerRecordType)
        {
            return validator.Add("isActive", "customFieldTooMany", CustomFieldsDbContext.MaxFieldsPerRecordType).ToResult();
        }
        db.Entry(field).Property(d => d.Version).OriginalValue = request.Version!.Value;
        if (request.SortOrder is { } order)
        {
            field.SortOrder = order;
        }
        db.Entry(field).Property(d => d.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(field, session));
    }

    /// <summary>The request's checks and, when they pass, its changes applied to the definition
    /// (the record type and type are already set).</summary>
    private static void Check(Validator validator, ModuleCatalog catalog, FieldDefinition field, SaveFieldRequest request)
    {
        var labelEn = request.LabelEn?.Trim() ?? "";
        // An Arabic label left out shows the English one on Arabic screens (a label is never blank).
        var labelAr = string.IsNullOrWhiteSpace(request.LabelAr) ? labelEn : request.LabelAr.Trim();
        validator.MaxLength("labelEn", labelEn, FieldRules.LabelMaxLength).MaxLength("labelAr", labelAr, FieldRules.LabelMaxLength);
        validator.Must(labelEn.Length == 0 || labelEn.Any(char.IsAsciiLetterOrDigit), "labelEn", "customFieldLabelLatin");
        var decimals = request.Decimals ?? field.Decimals;
        var type = field.FieldType;
        validator.Must(decimals is >= 0 and <= 8, "decimals", "customFieldDecimals");
        validator.Must(request.SortOrder is null or (>= 0 and <= 10000), "sortOrder", "customFieldSortOrder");
        decimals = Math.Clamp(decimals, 0, 8);

        var options = new List<CustomFieldOption>();
        if (type == CustomFieldTypes.Select)
        {
            var given = request.Options ?? [];
            if (given.Count is < 1 or > FieldRules.MaxOptions)
            {
                validator.Add("options", "customFieldOptionCount", FieldRules.MaxOptions);
            }
            foreach (var option in given.Take(FieldRules.MaxOptions))
            {
                var en = option.LabelEn?.Trim() ?? "";
                var ar = string.IsNullOrWhiteSpace(option.LabelAr) ? en : option.LabelAr.Trim();
                if (en.Length == 0 || en.Length > FieldRules.LabelMaxLength || ar.Length > FieldRules.LabelMaxLength)
                {
                    validator.Add("options", "customFieldOptionLabel");
                    continue;
                }
                var value = string.IsNullOrWhiteSpace(option.Value) ? FieldRules.OptionValueFor(en) : option.Value.Trim();
                if (!FieldRules.IsOptionValue(value))
                {
                    validator.Add("options", "customFieldOptionValue");
                    continue;
                }
                var unique = value;
                for (var n = 2; options.Any(o => string.Equals(o.Value, unique, StringComparison.OrdinalIgnoreCase)); n++)
                {
                    unique = value[..Math.Min(value.Length, FieldRules.OptionValueMaxLength - n.ToString().Length)] + n;
                }
                options.Add(new CustomFieldOption(unique, en, ar));
            }
        }

        string? lookup = null;
        if (type == CustomFieldTypes.Lookup)
        {
            lookup = request.LookupTarget?.Trim() ?? field.LookupTarget;
            validator.Required("lookupTarget", lookup);
            validator.Must(lookup is null || catalog.CustomFieldRecordTypes.Any(t => t.Table == lookup && t.Names is not null), "lookupTarget", "customFieldLookupTarget");
            validator.Must(field.LookupTarget is null || lookup == field.LookupTarget, "lookupTarget", "customFieldFixed");
        }

        string? defaultValue = null;
        if (!string.IsNullOrWhiteSpace(request.Default) && type != CustomFieldTypes.Lookup)
        {
            var text = request.Default.Trim();
            switch (type)
            {
                case CustomFieldTypes.Number when FieldRules.TryNumber(text, decimals, out var number):
                    defaultValue = FieldRules.NumberText(number, decimals);
                    break;
                case CustomFieldTypes.Date when FieldRules.TryDate(text, out var date):
                    defaultValue = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case CustomFieldTypes.Boolean when text is "true" or "false":
                    defaultValue = text;
                    break;
                case CustomFieldTypes.Select when options.Any(o => o.Value == text):
                    defaultValue = text;
                    break;
                case CustomFieldTypes.Text when text.Length <= CustomFieldsDbContext.MaxTextLength:
                    defaultValue = text;
                    break;
                default:
                    validator.Add("default", "customFieldDefault");
                    break;
            }
        }
        if (!validator.IsValid)
        {
            return;
        }
        field.LabelEn = labelEn;
        field.LabelAr = labelAr;
        field.Required = request.Required ?? false;
        field.DefaultValue = defaultValue;
        // A required field starts every new record with its default: without one, every add of a record would be
        // refused until the administrator knew the field existed, and a lookup (which has none) cannot be required.
        validator.Must(!field.Required || defaultValue is not null, "required", "customFieldRequiredNeedsDefault");
        if (!validator.IsValid)
        {
            return;
        }
        field.Decimals = type == CustomFieldTypes.Number ? decimals : 0;
        field.Options = options;
        field.LookupTarget = lookup;
        field.Searchable = type == CustomFieldTypes.Text && (request.Searchable ?? field.Searchable);
        field.IsActive = request.IsActive ?? field.IsActive;
    }

    private static FieldDto ToDto(FieldDefinition d, ErpDbSession session) => new(d.Id, d.RecordType, d.Key, d.LabelEn, d.LabelAr, d.FieldType, d.Required,
        d.DefaultValue, d.Decimals, d.Options, d.LookupTarget, d.SortOrder, d.Searchable, d.IsActive, d.CreatedAt, d.UpdatedAt, d.Version, session.HoldsWholeWorkspace);
}
