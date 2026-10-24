using Erp.Kernel.Data;
using Erp.Kernel.Seeding;
using Erp.Modules.Contacts.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NpgsqlTypes;

namespace Erp.Modules.Contacts.Seeding;

/// <summary>
/// Seeds the contacts directory: a few named organisations and people every demo and test can rely
/// on, then generated contacts up to the tenant's volume (the demo's main list: 100,000). With
/// <c>Erp:Seed:ContactsCsv</c> set (the shared comparison dataset's contacts.csv,
/// gauntlet/compare/data), the main demo tenant's generated contacts are exactly the dataset's, the
/// same ones the Odoo reference holds. Bulk rows go through <see cref="BulkInsert"/>: row-level
/// security checks every row and the audit trigger records it. A minimal or newly provisioned
/// workspace gets no contacts.
/// </summary>
internal sealed class ContactsSeeder(ContactsDbContext db, ErpDbSession session, IConfiguration configuration) : ITenantSeeder
{
    public const string ContactsCsvSetting = "Erp:Seed:ContactsCsv";

    /// <summary>How many contacts the main (first) tenant of the plan gets, when not its volume (the
    /// list timing budgets seed 100,000 contacts beside a few users).</summary>
    public const string ContactsVolumeSetting = "Erp:Seed:ContactsVolume";

    public int Order => 100;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (context.Plan.Profile is SeedProfile.Minimal or SeedProfile.Provision || await db.Contacts.AnyAsync(cancellationToken))
        {
            return;
        }
        var clock = new DateTimeOffset(2023, 6, 1, 6, 0, 0, TimeSpan.Zero);
        var rows = new List<SeedContact>();
        foreach (var named in NamedContacts.For(context))
        {
            rows.Add(named);
        }
        var volume = context.Tenant.Id == context.Plan.Tenants[0].Id && int.TryParse(configuration[ContactsVolumeSetting], out var configured) && configured >= 0
            ? configured
            : context.Tenant.Volume;
        var csv = configuration[ContactsCsvSetting];
        var shared = !string.IsNullOrWhiteSpace(csv) && context.Plan.Profile == SeedProfile.Demo && context.Tenant.Id == context.Plan.Tenants[0].Id;
        var bulk = shared
            ? SharedDatasetContacts.Read(csv!, volume)
            : DemoContacts.Generate(context.Tenant.VolumeRowsSeed, volume, NamedContacts.MailDomain(context), letterCodes: context.Plan.Profile == SeedProfile.Gate);
        rows.AddRange(bulk);

        // Ids in creation order, organisations known by reference before their people.
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var stamped = new List<(SeedContact Row, Guid Id, DateTimeOffset At)>(rows.Count);
        foreach (var row in rows)
        {
            clock = clock.AddSeconds(37);
            var id = Guid.CreateVersion7(clock);
            stamped.Add((row, id, clock));
            if (row.Code is { } reference)
            {
                ids[reference] = id;
            }
        }
        var columns = new BulkColumn[]
        {
            new("id", NpgsqlDbType.Uuid), new("tenant_id", NpgsqlDbType.Uuid), new("kind", NpgsqlDbType.Varchar), new("organisation_id", NpgsqlDbType.Uuid),
            new("code", NpgsqlDbType.Varchar), new("name_en", NpgsqlDbType.Varchar), new("name_ar", NpgsqlDbType.Varchar), new("job_title", NpgsqlDbType.Varchar),
            new("email", NpgsqlDbType.Varchar), new("phone", NpgsqlDbType.Varchar), new("mobile", NpgsqlDbType.Varchar), new("website", NpgsqlDbType.Varchar),
            new("address_line1", NpgsqlDbType.Varchar), new("area", NpgsqlDbType.Varchar), new("city", NpgsqlDbType.Varchar), new("emirate", NpgsqlDbType.Varchar),
            new("po_box", NpgsqlDbType.Varchar), new("country", NpgsqlDbType.Varchar), new("address_ar", NpgsqlDbType.Varchar), new("tags", NpgsqlDbType.Varchar),
            new("trade_licence_number", NpgsqlDbType.Varchar), new("tax_registration_number", NpgsqlDbType.Varchar), new("default_currency", NpgsqlDbType.Char),
            new("credit_limit_amount", NpgsqlDbType.Numeric), new("credit_limit_currency", NpgsqlDbType.Char), new("credit_limit_exchange_rate", NpgsqlDbType.Numeric),
            new("credit_limit_base_amount", NpgsqlDbType.Numeric), new("credit_limit_base_currency", NpgsqlDbType.Char), new("credit_limit_stated", NpgsqlDbType.Boolean),
            new("notes", NpgsqlDbType.Varchar), new("is_active", NpgsqlDbType.Boolean),
            new("name_key", NpgsqlDbType.Varchar), new("name_ar_key", NpgsqlDbType.Varchar), new("email_key", NpgsqlDbType.Varchar),
            new("phone_key", NpgsqlDbType.Varchar), new("mobile_key", NpgsqlDbType.Varchar),
            new("created_at", NpgsqlDbType.TimestampTz), new("updated_at", NpgsqlDbType.TimestampTz),
        };
        string? Mark(string? value) => value is null ? null : context.Mark(value);
        var values = stamped.Select(s =>
        {
            var r = s.Row;
            var organisation = r.OrganisationReference is { } parent && ids.TryGetValue(parent, out var parentId) ? parentId : (Guid?)null;
            var nameEn = Mark(r.NameEn) ?? "";
            var nameAr = Mark(r.NameAr) ?? "";
            var limit = r.CreditLimit is { } amount ? Erp.Kernel.Money.Money.Create(amount, Erp.Kernel.Money.CurrencyCode.Aed, 1m, Erp.Kernel.Money.CurrencyCode.Aed) : (Erp.Kernel.Money.Money?)null;
            return new object?[]
            {
                s.Id, context.Tenant.Id, r.Kind, r.Kind == ContactKinds.Person ? organisation : null,
                r.Code, nameEn, nameAr, Mark(r.JobTitle),
                r.Email, r.Phone, r.Mobile, r.Website,
                Mark(r.Street), Mark(r.Area), Mark(r.City), r.Country == ContactsValidation.UnitedArabEmirates ? r.Emirate : null,
                r.PoBox, r.Country, Mark(r.AddressAr), r.Tags,
                r.TradeLicence, r.TaxNumber, "AED",
                limit?.Amount, limit?.Currency, limit?.ExchangeRate, limit?.BaseAmount, limit?.BaseCurrency, limit?.IsStated,
                Mark(r.Notes), r.Active,
                ContactsValidation.NameKey(nameEn), ContactsValidation.NameKey(nameAr), ContactsValidation.EmailKey(r.Email),
                ContactsValidation.PhoneKey(r.Phone), ContactsValidation.PhoneKey(r.Mobile),
                s.At, s.At,
            };
        });
        await BulkInsert.InsertAsync(session, ContactsDbContext.SchemaName, "contacts", columns, values, cancellationToken);
    }
}

/// <summary>One contact to seed, before ids are given.</summary>
internal sealed record SeedContact(
    string Kind,
    string? Code,
    string NameEn,
    string NameAr,
    string? OrganisationReference = null,
    string? JobTitle = null,
    string? Email = null,
    string? Phone = null,
    string? Mobile = null,
    string? Website = null,
    string? Street = null,
    string? Area = null,
    string? City = null,
    string? Emirate = null,
    string? PoBox = null,
    string Country = ContactsValidation.UnitedArabEmirates,
    string? AddressAr = null,
    string? Tags = null,
    string? TradeLicence = null,
    string? TaxNumber = null,
    decimal? CreditLimit = null,
    string? Notes = null,
    bool Active = true);

/// <summary>The contacts every workspace with demo or gate data starts with (screens, tests and the
/// end-to-end suite name them). Fictional names; numbers in the UAE's 555 range.</summary>
internal static class NamedContacts
{
    /// <summary>The domain the contacts' e-mail addresses are under: the contacts' own businesses
    /// (example domains), never the workspace's (a customer is not staff), and in a tenant carrying a
    /// canary under the canary, so an address shown to another tenant is a leak the gates see.</summary>
    public static string MailDomain(TenantSeedContext context) => context.Tenant.Canary is { } canary ? $"{canary.ToLowerInvariant()}.example" : "example";

    public static IEnumerable<SeedContact> For(TenantSeedContext context)
    {
        var domain = MailDomain(context);
        yield return new SeedContact(ContactKinds.Organisation, "C-DEMO-1", "Falcon Building Materials LLC", "فالكون لمواد البناء ذ.م.م",
            Email: $"accounts@falcon.{domain}", Phone: "+971 4 555 0100", Website: "https://www.falcon-materials.example",
            Street: "Warehouse 12, Street 4", Area: "Al Quoz Industrial Area 3", City: "Dubai", Emirate: "dubai", PoBox: "12345",
            AddressAr: "مستودع 12، شارع 4، القوز الصناعية 3، دبي", Tags: "Customer; VIP", TradeLicence: "DED-778812", TaxNumber: "100234567800003",
            CreditLimit: 250_000m, Notes: "Pays by bank transfer within 30 days.");
        yield return new SeedContact(ContactKinds.Person, "C-DEMO-2", "Hamad Saeed Al Ketbi", "حمد سعيد الكتبي", OrganisationReference: "C-DEMO-1",
            JobTitle: "Purchasing Manager", Email: $"hamad.alketbi@falcon.{domain}", Phone: "+971 4 555 0101", Mobile: "+971 50 555 0102",
            City: "Dubai", Emirate: "dubai", Tags: "Customer");
        yield return new SeedContact(ContactKinds.Organisation, "C-DEMO-3", "Gulf Pipe Supplies FZE", "الخليج لتوريد الأنابيب م.م.ح",
            Email: $"sales@gulfpipe.{domain}", Phone: "+971 6 555 0300", Street: "Plot 7, Saif Zone", Area: "Saif Zone", City: "Sharjah", Emirate: "sharjah", PoBox: "8800",
            Tags: "Supplier", TradeLicence: "SAIF-55120", TaxNumber: "100345678900003", CreditLimit: 75_000m);
        yield return new SeedContact(ContactKinds.Person, "C-DEMO-4", "Priya Nair", "بريا ناير", OrganisationReference: "C-DEMO-3",
            JobTitle: "Sales Executive", Email: $"priya.nair@gulfpipe.{domain}", Mobile: "+971 55 555 0304", City: "Sharjah", Emirate: "sharjah", Tags: "Supplier");
        yield return new SeedContact(ContactKinds.Organisation, "C-DEMO-5", "Oasis Retail Trading LLC", "الواحة لتجارة التجزئة ذ.م.م",
            Email: $"info@oasis-retail.{domain}", Phone: "+971 2 555 0500", Area: "Mussafah", City: "Abu Dhabi", Emirate: "abuDhabi", Tags: "Customer; Retail",
            Active: false, Notes: "Archived: closed its Mussafah shop.");
        yield return new SeedContact(ContactKinds.Organisation, "C-DEMO-6", "Muscat Marine Services LLC", "مسقط للخدمات البحرية ش.م.م",
            Email: $"ops@muscatmarine.{domain}", Phone: "+968 2455 0600", City: "Muscat", Country: "OM", Tags: "Supplier; Export");
    }
}

/// <summary>Deterministic, realistic contacts for demo volume: about three in ten are
/// organisations; most people belong to one of the organisations made shortly before them.</summary>
internal static class DemoContacts
{
    private static readonly (string En, string Ar)[] FirstNames =
    [
        ("Ahmed", "أحمد"), ("Mohammed", "محمد"), ("Fatima", "فاطمة"), ("Aisha", "عائشة"), ("Omar", "عمر"), ("Khalid", "خالد"), ("Mariam", "مريم"),
        ("Noura", "نورة"), ("Hassan", "حسن"), ("Yousef", "يوسف"), ("Sara", "سارة"), ("Rashid", "راشد"), ("Hamdan", "حمدان"), ("Latifa", "لطيفة"),
        ("Saeed", "سعيد"), ("Huda", "هدى"), ("Ibrahim", "إبراهيم"), ("Reem", "ريم"), ("Abdullah", "عبدالله"), ("Salem", "سالم"),
        ("Rahul", "راهول"), ("Priya", "بريا"), ("Arjun", "أرجون"), ("Anjali", "أنجالي"), ("Imran", "عمران"), ("Ayesha", "عائشة"),
        ("Jose", "خوسيه"), ("Maria", "ماريا"), ("John", "جون"), ("Grace", "غريس"), ("Tariq", "طارق"), ("Zainab", "زينب"), ("Bilal", "بلال"),
        ("Faisal", "فيصل"), ("Amal", "أمل"), ("Karim", "كريم"), ("Rania", "رانيا"), ("Samir", "سمير"), ("Dina", "دينا"), ("Majid", "ماجد"),
    ];

    private static readonly (string En, string Ar)[] LastNames =
    [
        ("Al Mansoori", "المنصوري"), ("Al Nuaimi", "النعيمي"), ("Al Hashimi", "الهاشمي"), ("Al Suwaidi", "السويدي"), ("Al Mazrouei", "المزروعي"),
        ("Al Shamsi", "الشامسي"), ("Al Ketbi", "الكتبي"), ("Al Falasi", "الفلاسي"), ("Al Marzooqi", "المرزوقي"), ("Al Dhaheri", "الظاهري"),
        ("Haddad", "حداد"), ("Khoury", "خوري"), ("Nasser", "ناصر"), ("Saleh", "صالح"), ("Farouk", "فاروق"), ("Rahman", "رحمن"),
        ("Qureshi", "قريشي"), ("Sharma", "شارما"), ("Patel", "باتيل"), ("Nair", "ناير"), ("Menon", "مينون"), ("Khan", "خان"),
        ("Siddiqui", "صديقي"), ("Santos", "سانتوس"), ("Reyes", "رييس"), ("Garcia", "غارسيا"), ("Mansour", "منصور"), ("Darwish", "درويش"),
    ];

    private static readonly (string En, string Ar)[] BusinessWords =
    [
        ("Al Noor", "النور"), ("Falcon", "الصقر"), ("Gulf", "الخليج"), ("Emirates", "الإمارات"), ("Desert Rose", "وردة الصحراء"), ("Oasis", "الواحة"),
        ("Pearl", "اللؤلؤة"), ("Crescent", "الهلال"), ("Horizon", "الأفق"), ("Palm", "النخلة"), ("Corniche", "الكورنيش"), ("Marina", "المارينا"),
        ("Al Waha", "الواحة الخضراء"), ("Bright Star", "النجم الساطع"), ("Union", "الاتحاد"), ("Arabian", "العربية"), ("Sahara", "الصحراء"), ("Coral", "المرجان"),
    ];

    private static readonly (string En, string Ar)[] Trades =
    [
        ("Trading", "للتجارة"), ("Building Materials", "لمواد البناء"), ("Steel Industries", "للصناعات الحديدية"), ("Foodstuff Trading", "لتجارة المواد الغذائية"),
        ("Electromechanical", "للأعمال الكهروميكانيكية"), ("Logistics", "للخدمات اللوجستية"), ("Plastics", "للبلاستيك"), ("Auto Spare Parts", "لقطع غيار السيارات"),
        ("Packaging", "للتغليف"), ("Contracting", "للمقاولات"), ("Textiles", "للمنسوجات"), ("Furniture", "للأثاث"),
    ];

    private static readonly (string En, string Ar)[] Suffixes = [("LLC", "ذ.م.م"), ("FZE", "م.م.ح"), ("FZCO", "ش.م.ح"), ("Est.", "مؤسسة")];

    private static readonly (string Code, string City, string[] Areas, string PhoneArea)[] Places =
    [
        ("dubai", "Dubai", ["Deira", "Bur Dubai", "Al Quoz", "Jebel Ali", "Business Bay", "Al Barsha"], "4"),
        ("abuDhabi", "Abu Dhabi", ["Mussafah", "Al Reem Island", "Khalifa City", "Mina Zayed"], "2"),
        ("sharjah", "Sharjah", ["Industrial Area 4", "Al Nahda", "Al Majaz", "Saif Zone"], "6"),
        ("ajman", "Ajman", ["Al Jurf", "Al Rashidiya", "Ajman Free Zone"], "6"),
        ("rasAlKhaimah", "Ras Al Khaimah", ["Al Nakheel", "Al Hamra"], "7"),
        ("fujairah", "Fujairah", ["Fujairah City", "Dibba"], "9"),
        ("ummAlQuwain", "Umm Al Quwain", ["Al Salamah"], "6"),
    ];

    private static readonly string[] Tags = ["Customer", "Supplier", "Distributor", "Contractor", "Government", "VIP", "Export", "Retail"];

    private static readonly string[] JobTitles = ["Purchasing Manager", "Accountant", "Sales Executive", "Operations Manager", "General Manager", "Store Keeper", "Engineer"];

    /// <summary>
    /// <paramref name="count"/> contacts drawn from <paramref name="seed"/>, e-mail addresses under
    /// <paramref name="domain"/>. Phone numbers carry no zero after the area or mobile prefix: the
    /// gate fixture's companies and branches have numbers of zeros (+971 4 000 0002), and a contact
    /// whose number held those digits would answer a search for a company's number. With
    /// <paramref name="letterCodes"/> (the gate fixture) codes are letters (C-AAB), for the same
    /// reason: C000002 holds the digits of company numbers and PO boxes.
    /// </summary>
    public static IEnumerable<SeedContact> Generate(string seed, int count, string domain, bool letterCodes = false)
    {
        var random = new Random(StableHash(seed));
        var organisations = new List<string>();
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 1; i <= count; i++)
        {
            var reference = letterCodes ? $"C-{Letters(i)}" : $"C{i:000000}";
            var place = Places[random.Next(Places.Length)];
            var area = place.Areas[random.Next(place.Areas.Length)];
            var poBox = random.Next(10) < 6 ? random.Next(1000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            if (i <= 10 || random.Next(10) < 3)
            {
                var word = BusinessWords[random.Next(BusinessWords.Length)];
                var trade = Trades[random.Next(Trades.Length)];
                var suffix = Suffixes[random.Next(Suffixes.Length)];
                // A name repeats with a number after it (Falcon Trading LLC 2), as a second company of
                // that name is told apart; the first keeps the name alone.
                var baseEn = $"{word.En} {trade.En} {suffix.En}";
                var seen = names[baseEn] = names.GetValueOrDefault(baseEn) + 1;
                var number = seen == 1 ? "" : $" {seen}";
                var nameEn = baseEn + number;
                var nameAr = (suffix.Ar == "مؤسسة" ? $"مؤسسة {word.Ar} {trade.Ar}" : $"{word.Ar} {trade.Ar} {suffix.Ar}") + number;
                var slug = new string(word.En.ToLowerInvariant().Where(char.IsAsciiLetterLower).ToArray());
                organisations.Add(reference);
                yield return new SeedContact(ContactKinds.Organisation, reference, nameEn, nameAr,
                    Email: $"info{i}@{slug}{i}.{domain}", Phone: $"+971 {place.PhoneArea} {NoZeros(random, 3)} {NoZeros(random, 4)}",
                    Website: $"https://www.{slug}{i}.example", Street: $"{random.Next(1, 120)} Street {random.Next(1, 40)}", Area: area, City: place.City,
                    Emirate: place.Code, PoBox: poBox, Tags: random.Next(10) < 8 ? Tags[random.Next(Tags.Length)] : null,
                    TradeLicence: random.Next(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    TaxNumber: random.Next(10) < 8 ? $"100{random.NextInt64(0, 999_999_999_999):000000000000}" : null,
                    CreditLimit: random.Next(501) * 1000m, Active: random.Next(40) != 0);
            }
            else
            {
                var first = FirstNames[random.Next(FirstNames.Length)];
                var middle = FirstNames[random.Next(FirstNames.Length)];
                var last = LastNames[random.Next(LastNames.Length)];
                var local = new string($"{first.En}.{last.En}".ToLowerInvariant().Where(c => char.IsAsciiLetterLower(c) || c == '.').ToArray());
                var parent = organisations.Count > 0 && random.Next(10) < 6 ? organisations[random.Next(Math.Max(0, organisations.Count - 400), organisations.Count)] : null;
                yield return new SeedContact(ContactKinds.Person, reference, $"{first.En} {middle.En} {last.En}", $"{first.Ar} {middle.Ar} {last.Ar}",
                    OrganisationReference: parent, JobTitle: parent is null ? null : JobTitles[random.Next(JobTitles.Length)],
                    Email: $"{local}.{i}@mail.{domain}", Mobile: $"+971 5{"024568"[random.Next(6)]} {NoZeros(random, 3)} {NoZeros(random, 4)}",
                    Area: area, City: place.City, Emirate: place.Code, Tags: random.Next(5) == 0 ? Tags[random.Next(Tags.Length)] : null,
                    Active: random.Next(50) != 0);
            }
        }
    }

    /// <summary>Digits 1 to 9 only.</summary>
    private static string NoZeros(Random random, int length) =>
        new(Enumerable.Range(0, length).Select(_ => (char)('1' + random.Next(9))).ToArray());

    /// <summary>A number written in capital letters (1 is AAB, 26 is ABA): a code without digits.</summary>
    private static string Letters(int number)
    {
        var letters = new char[3];
        for (var i = 2; i >= 0; i--)
        {
            letters[i] = (char)('A' + number % 26);
            number /= 26;
        }
        return number == 0 ? new string(letters) : Letters(number) + new string(letters);
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }
}

/// <summary>
/// The contacts of the shared comparison dataset (gauntlet/compare/data/generate.mjs, contacts.csv:
/// ref, kind, name, name_ar, parent_ref, email, phone, mobile, street, area, city, emirate_code,
/// po_box, country_code, website, trade_licence, trn, tags, currency, credit_limit), as the Odoo
/// reference loads them, so both products hold the same records.
/// </summary>
internal static class SharedDatasetContacts
{
    private static readonly Dictionary<string, string> EmirateCodes = new(StringComparer.Ordinal)
    {
        ["DU"] = "dubai", ["AZ"] = "abuDhabi", ["SH"] = "sharjah", ["AJ"] = "ajman", ["RK"] = "rasAlKhaimah", ["FU"] = "fujairah", ["UQ"] = "ummAlQuwain",
    };

    public static IEnumerable<SeedContact> Read(string path, int count)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{ContactsSeeder.ContactsCsvSetting} names {path}, which does not exist. Generate it with node gauntlet/compare/data/generate.mjs.", path);
        }
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        var header = Split(reader.ReadLine() ?? "");
        int Column(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new InvalidDataException($"{path}: no column '{name}'");
        var c = new
        {
            Ref = Column("ref"), Kind = Column("kind"), Name = Column("name"), NameAr = Column("name_ar"), Parent = Column("parent_ref"), Email = Column("email"),
            Phone = Column("phone"), Mobile = Column("mobile"), Street = Column("street"), Area = Column("area"), City = Column("city"), Emirate = Column("emirate_code"),
            PoBox = Column("po_box"), Country = Column("country_code"), Website = Column("website"), Licence = Column("trade_licence"), Trn = Column("trn"),
            Tags = Column("tags"), Limit = Column("credit_limit"),
        };
        var rows = new List<SeedContact>(Math.Max(0, count));
        while (rows.Count < count && reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }
            var cells = Split(line);
            string? Cell(int i) => i < cells.Length && cells[i].Length > 0 ? cells[i] : null;
            var country = Cell(c.Country) ?? ContactsValidation.UnitedArabEmirates;
            rows.Add(new SeedContact(
                Cell(c.Kind) == "company" ? ContactKinds.Organisation : ContactKinds.Person, Cell(c.Ref), Cell(c.Name) ?? "", Cell(c.NameAr) ?? "",
                OrganisationReference: Cell(c.Parent), Email: Cell(c.Email), Phone: Cell(c.Phone), Mobile: Cell(c.Mobile), Website: Cell(c.Website),
                Street: Cell(c.Street), Area: Cell(c.Area), City: Cell(c.City),
                Emirate: country == ContactsValidation.UnitedArabEmirates && Cell(c.Emirate) is { } code ? EmirateCodes.GetValueOrDefault(code) : null,
                PoBox: Cell(c.PoBox), Country: country,
                Tags: Cell(c.Tags) is { } tags ? ContactsValidation.JoinTags(tags.Split(';')) : null,
                TradeLicence: Cell(c.Licence), TaxNumber: Cell(c.Trn),
                CreditLimit: Cell(c.Limit) is { } limit ? decimal.Parse(limit, System.Globalization.CultureInfo.InvariantCulture) : null));
        }
        return rows;
    }

    /// <summary>One CSV line (RFC 4180 quoting; the generator writes no line breaks inside cells).</summary>
    public static string[] Split(string line)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(ch);
        }
        cells.Add(cell.ToString());
        return [.. cells];
    }
}
