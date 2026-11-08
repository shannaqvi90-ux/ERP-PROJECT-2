using System.Collections.Immutable;
using Erp.Kernel.CustomFields;
using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Seeding;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Erp.Modules.CustomFields.Seeding;

/// <summary>
/// Gives the demo and gate workspaces custom fields on contacts, with values on every contact (the
/// demo's 100,000 included), so filtering by a custom field has volume to prove itself on. A
/// minimal or newly provisioned workspace defines none. The gate workspaces differ from each other
/// (tenant B's labels and texts carry its canary), so the isolation gates have answers that tell the
/// two apart. Runs after the contacts seeder and reads the contacts' ids through the contacts
/// module's registration, never its tables.
/// </summary>
internal sealed class CustomFieldsSeeder(CustomFieldsDbContext db, ModuleCatalog catalog, ErpDbSession session) : ITenantSeeder
{
    public const string ContactsTable = "contacts.contacts";

    public int Order => 140;

    /// <summary>How many contacts of a gate workspace get values.</summary>
    public const int GateValuedContacts = 6;

    public static readonly ImmutableArray<string> Segments = ["retail", "wholesale", "government", "keyAccount", "distributor"];

    public static readonly ImmutableArray<string> Managers =
    [
        "Rashid Al Mansoori", "Mariam Al Ketbi", "Hamdan Al Suwaidi", "Noura Al Dhaheri", "Khalid Al Shamsi", "Aisha Al Nuaimi",
        "Saeed Al Mazrouei", "Fatima Al Kaabi", "Omar Al Hashimi", "Latifa Al Falasi", "Yousef Al Zaabi", "Hessa Al Marri",
    ];

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        var record = catalog.CustomFieldRecordTypes.FirstOrDefault(r => r.Table == ContactsTable);
        if (context.Plan.Profile is SeedProfile.Minimal or SeedProfile.Provision || record?.AllIds is null || await db.Definitions.AnyAsync(cancellationToken))
        {
            return;
        }
        var demo = context.Plan.Profile == SeedProfile.Demo;
        var segment = Define(ContactsTable, "cfCustomerSegment", context.Mark("Customer segment"), context.Mark("شريحة العميل"), CustomFieldTypes.Select, 10,
            options: [new("retail", "Retail", "تجزئة"), new("wholesale", "Wholesale", "جملة"), new("government", "Government", "حكومي"),
                      new("keyAccount", "Key account", "حساب رئيسي"), new("distributor", "Distributor", "موزع")]);
        // The gate workspaces' segment starts at "retail" for a new contact, so the write-set gate sees
        // a contact's create write its custom field value (permission-write-sets.txt).
        segment.DefaultValue = demo ? null : "retail";
        // Neither workspace's account manager is searched by the list's search box: every searched custom field adds
        // an OR over its values to each search, and at 100,000 contacts that took the contacts timing budget's
        // Arabic search to 701 ms (budget 400 ms median) and 'al' to 384 ms. A tenant switches searching on per
        // field (tests/Erp.Modules.CustomFields.Tests defines its own searched field and covers search and its isolation).
        var manager = Define(ContactsTable, "cfAccountManager", context.Mark("Account manager"), context.Mark("مدير الحساب"), CustomFieldTypes.Text, 20);
        // The demo's other two fields (a date and a yes/no) add nothing the gates need.
        var review = Define(ContactsTable, "cfCreditReview", "Credit review date", "تاريخ مراجعة الائتمان", CustomFieldTypes.Date, 30);
        var vip = Define(ContactsTable, "cfPreferredPartner", "Preferred partner", "شريك مفضل", CustomFieldTypes.Boolean, 40);
        db.Definitions.AddRange(demo ? [segment, manager, review, vip] : [segment, manager]);
        await db.SaveChangesAsync(cancellationToken);

        // The gate workspaces hold values on their first contacts only: the isolation gates seed them at the
        // volume of a demo, and every value is an audit row, which a printed or exported audit log pays for.
        var ids = await record.AllIds(context.Services, cancellationToken);
        if (!demo)
        {
            ids = ids.Take(GateValuedContacts).ToList();
        }
        var random = new Random(StableSeed(context.Tenant.VolumeRowsSeed));
        var at = new DateTimeOffset(2024, 1, 15, 8, 0, 0, TimeSpan.Zero);
        var columns = new BulkColumn[]
        {
            new("id", NpgsqlDbType.Uuid), new("tenant_id", NpgsqlDbType.Uuid), new("record_type", NpgsqlDbType.Varchar), new("record_id", NpgsqlDbType.Uuid),
            new("field_id", NpgsqlDbType.Uuid), new("code", NpgsqlDbType.Varchar), new("text_value", NpgsqlDbType.Varchar), new("number_value", NpgsqlDbType.Numeric), new("date_value", NpgsqlDbType.Date),
            new("bool_value", NpgsqlDbType.Boolean), new("ref_value", NpgsqlDbType.Uuid), new("created_at", NpgsqlDbType.TimestampTz), new("updated_at", NpgsqlDbType.TimestampTz),
        };
        var codes = new[] { segment, manager, review, vip }.ToDictionary(d => d.Id, d => d.Key);
        object?[] Row(Guid record, Guid field, string? text = null, DateOnly? date = null, bool? flag = null) =>
            [Guid.CreateVersion7(at), context.Tenant.Id, ContactsTable, record, field, codes[field], text, null, date, flag, null, at, at];

        IEnumerable<object?[]> Rows()
        {
            foreach (var id in ids)
            {
                yield return Row(id, segment.Id, Segments[random.Next(Segments.Length)]);
                yield return Row(id, manager.Id, context.Mark(Managers[random.Next(Managers.Length)]));
                if (!demo)
                {
                    continue;
                }
                if (random.Next(10) < 3)
                {
                    yield return Row(id, review.Id, date: new DateOnly(2025, 1, 1).AddDays(random.Next(0, 730)));
                }
                if (random.Next(10) == 0)
                {
                    yield return Row(id, vip.Id, flag: true);
                }
            }
        }
        await BulkInsert.InsertAsync(session, CustomFieldsDbContext.SchemaName, "field_values", columns, Rows(), cancellationToken);
    }

    private static FieldDefinition Define(string recordType, string key, string en, string ar, string type, int order, bool searchable = false, CustomFieldOption[]? options = null) => new()
    {
        RecordType = recordType, Key = key, LabelEn = en, LabelAr = ar, FieldType = type, SortOrder = order, Searchable = searchable,
        Decimals = type == CustomFieldTypes.Number ? 2 : 0, Options = options?.ToList() ?? [],
    };

    private static int StableSeed(string text)
    {
        var hash = 17;
        foreach (var c in text)
        {
            hash = unchecked(hash * 31 + c);
        }
        return hash;
    }
}
