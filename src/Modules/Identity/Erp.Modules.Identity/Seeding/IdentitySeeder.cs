using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Kernel.Seeding;
using Erp.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NpgsqlTypes;

namespace Erp.Modules.Identity.Seeding;

/// <summary>
/// Seeds the Administrator system role (kept in step with the permission catalogue on every run),
/// a read-only role, named demo users and, for the demo profile, bulk users up to the tenant's
/// volume so the user list carries Odoo-comparable volume. With <c>Erp:Seed:UsersCsv</c> set (the
/// shared comparison dataset's users.csv, gauntlet/compare/data), the main demo tenant's bulk users
/// are exactly the dataset's users, the same ones the Odoo reference holds.
/// </summary>
internal sealed class IdentitySeeder(IdentityDbContext db, ModuleCatalog catalog, ErpDbSession session, IConfiguration configuration) : ITenantSeeder
{
    public const string AdministratorKey = "administrator";

    public const string UsersCsvSetting = "Erp:Seed:UsersCsv";

    public int Order => 10;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        var all = catalog.PermissionKeys.Order(StringComparer.Ordinal).ToList();
        var administrator = await db.Roles.SingleOrDefaultAsync(r => r.SystemKey == AdministratorKey, cancellationToken);
        if (administrator is not null)
        {
            if (!administrator.Permissions.SequenceEqual(all))
            {
                administrator.Permissions = all;
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        administrator = new Role
        {
            NameEn = "Administrator",
            NameAr = "مدير النظام",
            IsSystem = true,
            SystemKey = AdministratorKey,
            Permissions = all,
        };
        var readOnly = new Role
        {
            NameEn = context.Mark("Read-only"),
            NameAr = context.Mark("قراءة فقط"),
            Permissions = all.Where(p => p.EndsWith(".read", StringComparison.Ordinal)).Append(IdentityPermissions.ProfileUpdate).Order(StringComparer.Ordinal).ToList(),
        };
        db.Roles.AddRange(administrator, readOnly);

        var hash = PasswordHasher.Hash(context.Plan.DemoPassword);
        var now = DateTimeOffset.UtcNow;
        var domain = context.Tenant.EmailDomain;
        var people = new (string Local, string Name, string Language, Role? Role)[]
        {
            ("admin", "Mariam Al Mansoori", "en", administrator),
            ("admin.ar", "فاطمة الزعابي", "ar", administrator),
            ("viewer", "Omar Haddad", "en", readOnly),
            ("noaccess", "Layla Nasser", "en", null),
        };
        foreach (var (local, name, language, role) in people)
        {
            var email = $"{local}@{domain}";
            var user = new User
            {
                Email = email,
                EmailNormalized = email.ToLowerInvariant(),
                DisplayName = context.Mark(name),
                Language = language,
            };
            db.Users.Add(user);
            db.Credentials.Add(new UserCredential { Id = user.Id, PasswordHash = hash, ChangedAt = now });
            if (role is not null)
            {
                db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        if (context.Tenant.Volume > 0)
        {
            await SeedVolumeAsync(context, hash, cancellationToken);
        }
    }

    private async Task SeedVolumeAsync(TenantSeedContext context, string hash, CancellationToken cancellationToken)
    {
        var columns = new BulkColumn[]
        {
            new("id", NpgsqlDbType.Uuid),
            new("tenant_id", NpgsqlDbType.Uuid),
            new("email", NpgsqlDbType.Varchar),
            new("email_normalized", NpgsqlDbType.Varchar),
            new("display_name", NpgsqlDbType.Varchar),
            new("language", NpgsqlDbType.Varchar),
            new("is_active", NpgsqlDbType.Boolean),
            new("created_at", NpgsqlDbType.TimestampTz),
            new("updated_at", NpgsqlDbType.TimestampTz),
        };
        var tenantId = context.Tenant.Id;
        var start = new DateTimeOffset(2024, 1, 1, 6, 0, 0, TimeSpan.Zero);
        var csv = configuration[UsersCsvSetting];
        var shared = !string.IsNullOrWhiteSpace(csv) && context.Plan.Profile == SeedProfile.Demo && context.Tenant.Id == context.Plan.Tenants[0].Id;
        var people = shared
            ? SharedDatasetUsers.Read(csv!, context.Tenant.Volume)
            : DemoPeople.Generate(context.Tenant.Code, context.Tenant.Volume).Select((p, i) => p with { EmailLocal = $"{p.EmailLocal}.{i + 1}@{context.Tenant.EmailDomain}" });
        var rows = people.Select((p, i) =>
        {
            var email = p.EmailLocal;
            var created = start.AddMinutes(i * 7L);
            return new object?[]
            {
                Guid.CreateVersion7(created), tenantId, email, email.ToLowerInvariant(), context.Mark(p.DisplayName),
                p.Language, i % 23 != 0, created, created,
            };
        });
        await BulkInsert.InsertAsync(session, IdentityDbContext.SchemaName, "users", columns, rows, cancellationToken);

        // Their credentials, in one statement: the application role may write a hash but not read
        // one, so the bulk loader (which copies the target's shape) cannot be used here.
        await using var command = new Npgsql.NpgsqlCommand("""
            INSERT INTO identity.user_credentials (id, tenant_id, password_hash, must_change, expires_at, changed_at)
            SELECT u.id, u.tenant_id, @hash, false, NULL, u.created_at
              FROM identity.users u
             WHERE NOT EXISTS (SELECT 1 FROM identity.user_credentials c WHERE c.id = u.id)
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("hash", hash);
        // As long as the bulk load itself may take (100,000 rows on a busy machine).
        command.CommandTimeout = 600;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// The users of the shared comparison dataset (gauntlet/compare/data/generate.mjs, users.csv:
/// ref, name, name_ar, login, lang), with the name and language the Odoo reference loads for
/// them. The e-mail is the dataset's login; <see cref="DemoPeople.Person.EmailLocal"/> carries
/// the whole address.
/// </summary>
internal static class SharedDatasetUsers
{
    public static IEnumerable<DemoPeople.Person> Read(string path, int count)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{IdentitySeeder.UsersCsvSetting} names {path}, which does not exist. Generate it with node gauntlet/compare/data/generate.mjs.", path);
        }
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        var header = Split(reader.ReadLine() ?? "");
        int Column(string name) => Array.IndexOf(header, name) is var i and >= 0 ? i : throw new InvalidDataException($"{path}: no column '{name}'");
        int nameColumn = Column("name"), loginColumn = Column("login"), languageColumn = Column("lang");
        var people = new List<DemoPeople.Person>(Math.Max(0, count));
        while (people.Count < count && reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }
            var cells = Split(line);
            people.Add(new DemoPeople.Person(cells[nameColumn], cells[loginColumn], cells[languageColumn] == "ar" ? "ar" : "en"));
        }
        return people;
    }

    /// <summary>One CSV line (RFC 4180 quoting; the generator writes no line breaks inside cells).</summary>
    public static string[] Split(string line)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return [.. cells];
    }
}

/// <summary>Deterministic, realistic UAE workforce names for demo volume.</summary>
internal static class DemoPeople
{
    private static readonly string[] FirstNames =
    [
        "Ahmed", "Mohammed", "Fatima", "Aisha", "Omar", "Khalid", "Mariam", "Noura", "Hassan", "Yousef",
        "Sara", "Rashid", "Hamdan", "Latifa", "Saeed", "Huda", "Ibrahim", "Reem", "Abdullah", "Shamma",
        "Rahul", "Priya", "Arjun", "Anjali", "Vikram", "Deepa", "Suresh", "Kavya", "Imran", "Ayesha",
        "Jose", "Maria", "Mark", "Grace", "John", "Angela", "Michael", "Sophie", "David", "Elena",
        "Tariq", "Zainab", "Bilal", "Hina", "Faisal", "Amal", "Karim", "Rania", "Samir", "Dina",
    ];

    private static readonly string[] LastNames =
    [
        "Al Mansoori", "Al Nuaimi", "Al Hashimi", "Al Suwaidi", "Al Mazrouei", "Al Shamsi", "Al Ketbi", "Al Falasi",
        "Al Marzooqi", "Al Dhaheri", "Haddad", "Khoury", "Nasser", "Saleh", "Hamdan", "Farouk", "Rahman", "Qureshi",
        "Sharma", "Patel", "Nair", "Menon", "Iyer", "Reddy", "Khan", "Siddiqui", "Santos", "Reyes", "Cruz", "Garcia",
        "Smith", "Brown", "Wilson", "Taylor", "Ivanova", "Petrov", "Haddadin", "Mansour", "Aziz", "Darwish",
    ];

    private static readonly string[] ArabicFirstNames =
    [
        "أحمد", "محمد", "فاطمة", "عائشة", "عمر", "خالد", "مريم", "نورة", "حسن", "يوسف",
        "سارة", "راشد", "حمدان", "لطيفة", "سعيد", "هدى", "إبراهيم", "ريم", "عبدالله", "شمّة",
    ];

    private static readonly string[] ArabicLastNames =
    [
        "المنصوري", "النعيمي", "الهاشمي", "السويدي", "المزروعي", "الشامسي", "الكتبي", "الفلاسي", "المرزوقي", "الظاهري",
    ];

    public sealed record Person(string DisplayName, string EmailLocal, string Language);

    public static IEnumerable<Person> Generate(string seed, int count)
    {
        var random = new Random(StableHash(seed));
        for (var i = 0; i < count; i++)
        {
            if (random.Next(5) == 0)
            {
                var a = random.Next(ArabicFirstNames.Length);
                var b = random.Next(ArabicLastNames.Length);
                yield return new Person($"{ArabicFirstNames[a]} {ArabicLastNames[b]}", $"{Ascii(FirstNames[a])}.{Ascii(LastNames[b])}", "ar");
            }
            else
            {
                var first = FirstNames[random.Next(FirstNames.Length)];
                var last = LastNames[random.Next(LastNames.Length)];
                yield return new Person($"{first} {last}", $"{Ascii(first)}.{Ascii(last)}", "en");
            }
        }
    }

    private static string Ascii(string value) =>
        new string(value.ToLowerInvariant().Where(char.IsAsciiLetterLower).ToArray());

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in value)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }
}
