using System.Text.Json;
using Erp.Kernel.Audit;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Shell;
using Erp.Modules.CustomFields.Contracts;
using Erp.Modules.CustomFields.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.CustomFields;

/// <summary>
/// Tenant-defined custom fields (p09): an administrator adds fields (text, decimal number, date,
/// yes/no, single select, lookup) to the kinds of records modules register, with English and Arabic
/// labels, a required flag and a default. The fields then appear by themselves in the record's form,
/// its list (column chooser, filters, search), its API and the audit trail. Definitions are
/// workspace-wide settings; a record's values are stored in typed, indexed columns (one row per
/// record and field) and follow the record's own permissions, because they travel with the record's
/// endpoints (docs/decisions/p09-custom-fields-storage-and-filtering.md).
/// </summary>
public sealed class CustomFieldsModule : ErpModule
{
    public override string Name => "customfields";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions([.. CustomFieldsPermissions.All]);
        module.DbContext<CustomFieldsDbContext>();
        module.Services.AddScoped<CustomFieldService>();
        module.Services.AddScoped<ICustomFields>(services => services.GetRequiredService<CustomFieldService>());
        module.Endpoints(FieldEndpoints.Map);
        module.Menu(new MenuEntry("customfields.fields", "customfields.menu.fields", "/customfields/fields", CustomFieldsPermissions.FieldsRead, Order: 865, Group: "settings"));
        module.List(FieldsList.Create(module.CustomFieldRecordTypesSoFar));
        // Reports print the list exactly as the screen shows it.
        module.ListRows(FieldsList.Key, async (services, request, http, cancellationToken) =>
            (await FieldEndpoints.PageAsync(services.GetRequiredService<CustomFieldsDbContext>(), services.GetRequiredService<ModuleCatalog>(), request, http, cancellationToken)).Map(r => (object)r));
        // The audit trail shows these records' history to whoever may read them (p07).
        module.AuditedRecord(new AuditRecordType("customfields.definitions", "customfields.record.field", CustomFieldsPermissions.FieldsRead, "/customfields/fields/{id}"));
        module.AuditedRecord(new AuditRecordType("customfields.field_values", "customfields.record.value", CustomFieldsPermissions.FieldsRead, null));
        module.Seeder<CustomFieldsSeeder>();
    }
}

/// <summary>One field a workspace added to a kind of record. A setting every company of the
/// workspace shares, so only someone who works in every company changes it.</summary>
public sealed class FieldDefinition : TenantEntity, IWorkspaceWide
{
    /// <summary>The kind of record (<c>schema.table</c> of a registered <see cref="CustomFieldRecordType"/>).</summary>
    public string RecordType { get; set; } = "";

    /// <summary>The machine name: <c>cf</c> and the English label in camel case. Never changes.</summary>
    public string Key { get; set; } = "";

    public string LabelEn { get; set; } = "";
    public string LabelAr { get; set; } = "";

    /// <summary>One of <see cref="CustomFieldTypes"/>. Never changes (values of the old kind would not fit).</summary>
    public string FieldType { get; set; } = CustomFieldTypes.Text;

    public bool Required { get; set; }

    /// <summary>The value a new record starts with, as canonical text.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>For a number: decimals kept (0 to 8).</summary>
    public int Decimals { get; set; } = 2;

    /// <summary>For a single select: its options, in display order.</summary>
    public List<CustomFieldOption> Options { get; set; } = [];

    /// <summary>For a lookup: the kind of record it points at.</summary>
    public string? LookupTarget { get; set; }

    public int SortOrder { get; set; }

    /// <summary>A text field the list's search box also matches.</summary>
    public bool Searchable { get; set; }

    /// <summary>False once switched off: hidden from forms, lists and the API; its values are kept.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>A record's value of a field: one typed column holds it (the others are null), so each
/// kind of value has its own index. Rows exist only for values that are set.</summary>
public sealed class FieldValue : TenantEntity
{
    public string RecordType { get; set; } = "";
    public Guid RecordId { get; set; }
    public Guid FieldId { get; set; }

    /// <summary>The field's machine name (kept with the value, which never changes it): the audit
    /// trail names a value by the field it belongs to.</summary>
    public string Code { get; set; } = "";

    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public DateOnly? DateValue { get; set; }
    public bool? BoolValue { get; set; }
    public Guid? RefValue { get; set; }
}

public sealed class CustomFieldsDbContext(DbContextOptions<CustomFieldsDbContext> options, ITenantContext? tenant = null) : ModuleDbContext(options, tenant)
{
    public const string SchemaName = "customfields";

    /// <summary>Most fields one kind of record may have: forms and lists stay usable.</summary>
    public const int MaxFieldsPerRecordType = 40;

    /// <summary>Longest text value.</summary>
    public const int MaxTextLength = 500;

    protected override string Schema => SchemaName;

    public DbSet<FieldDefinition> Definitions => Set<FieldDefinition>();
    public DbSet<FieldValue> Values => Set<FieldValue>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FieldDefinition>(e =>
        {
            e.ToTable("definitions", t =>
            {
                t.HasCheckConstraint("ck_definitions_type", "field_type BETWEEN 0 AND 5");
                t.HasCheckConstraint("ck_definitions_key", "code ~ '^cf[A-Za-z0-9]{1,38}$'");
                t.HasCheckConstraint("ck_definitions_labels", "btrim(name_en) <> '' AND btrim(name_ar) <> ''");
                t.HasCheckConstraint("ck_definitions_decimals", "decimals BETWEEN 0 AND 8");
                t.HasCheckConstraint("ck_definitions_lookup", "(field_type = 5) = (lookup_target IS NOT NULL)");
                t.HasCheckConstraint("ck_definitions_options", "(field_type = 4 AND jsonb_array_length(options) BETWEEN 1 AND 50) OR (field_type <> 4 AND jsonb_array_length(options) = 0)");
            });
            e.Property(x => x.RecordType).HasMaxLength(100);
            // The audit trail names a record by its name_en, name_ar and code columns: a field is named by
            // its labels and its machine name.
            e.Property(x => x.Key).HasColumnName("code").HasMaxLength(40);
            e.Property(x => x.LabelEn).HasColumnName("name_en").HasMaxLength(100);
            e.Property(x => x.LabelAr).HasColumnName("name_ar").HasMaxLength(100);
            // Kept as a small number (0 text, 1 number, 2 date, 3 yes/no, 4 single select, 5 lookup): the
            // API's words are not the workspace's data (a "date" among the values would be taken for a
            // tenant's secret wherever a screen or a printout says "date").
            e.Property(x => x.FieldType).HasColumnType("smallint").HasConversion(v => FieldTypeCodes.Code(v), c => FieldTypeCodes.Name(c));
            e.Property(x => x.DefaultValue).HasMaxLength(MaxTextLength);
            e.Property(x => x.LookupTarget).HasMaxLength(100);
            e.Property(x => x.Required).HasDefaultValue(false);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.Options).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<CustomFieldOption>>(v, (JsonSerializerOptions?)null) ?? new List<CustomFieldOption>(),
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<CustomFieldOption>>(
                        (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                        v => JsonSerializer.Deserialize<List<CustomFieldOption>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
            // One field of a name per kind of record (also after it is switched off: its values stay).
            e.HasIndex(x => new { x.TenantId, x.RecordType, x.Key }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RecordType, x.SortOrder });
        });

        modelBuilder.Entity<FieldValue>(e =>
        {
            e.ToTable("field_values", t =>
            {
                // Exactly one typed column is set: a value is never two things.
                t.HasCheckConstraint("ck_field_values_one_column", "num_nonnulls(text_value, number_value, date_value, bool_value, ref_value) = 1");
            });
            e.Property(x => x.RecordType).HasMaxLength(100);
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.TextValue).HasMaxLength(MaxTextLength);
            e.Property(x => x.NumberValue).HasPrecision(28, 8);
            // A record holds one value per field; this index also reads a record's values.
            e.HasIndex(x => new { x.TenantId, x.RecordId, x.FieldId }).IsUnique();
            // A field's values by kind, for the match functions (filters) and sorting by value.
            e.HasIndex(x => new { x.TenantId, x.FieldId, x.NumberValue, x.RecordId }).HasFilter("number_value IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.FieldId, x.DateValue, x.RecordId }).HasFilter("date_value IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.FieldId, x.BoolValue, x.RecordId }).HasFilter("bool_value IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.FieldId, x.RefValue, x.RecordId }).HasFilter("ref_value IS NOT NULL");
            // The values belong to a definition of the same workspace; fields are switched off, never deleted.
            e.HasOne<FieldDefinition>().WithMany().HasForeignKey(x => new { x.TenantId, x.FieldId })
                .HasPrincipalKey(d => new { d.TenantId, d.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

/// <summary>How <see cref="FieldDefinition.FieldType"/> is stored.</summary>
internal static class FieldTypeCodes
{
    public static short Code(string type) => (short)CustomFieldTypes.All.ToList().IndexOf(type) is var index and >= 0
        ? index
        : throw new ArgumentOutOfRangeException(nameof(type), type, "not a custom field type");

    public static string Name(short code) => code >= 0 && code < CustomFieldTypes.All.Count
        ? CustomFieldTypes.All[code]
        : throw new ArgumentOutOfRangeException(nameof(code), code, "not a custom field type");
}

internal sealed class CustomFieldsDbContextDesignFactory : IDesignTimeDbContextFactory<CustomFieldsDbContext>
{
    public CustomFieldsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CustomFieldsDbContext>();
        DataRegistration.ConfigureNpgsql(options, "Host=localhost;Database=design", CustomFieldsDbContext.SchemaName);
        return new CustomFieldsDbContext(options.Options);
    }
}
