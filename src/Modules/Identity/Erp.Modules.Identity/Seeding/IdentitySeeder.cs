using Erp.Kernel.Data;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Kernel.Seeding;
using Erp.Modules.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Erp.Modules.Identity.Seeding;

/// <summary>
/// Seeds the Administrator system role (kept in step with the permission catalogue on every run),
/// a read-only role, named demo users and, for the demo profile, bulk users up to the tenant's
/// volume so the user list carries Odoo-comparable volume.
/// </summary>
internal sealed class IdentitySeeder(IdentityDbContext db, ModuleCatalog catalog, ErpDbSession session) : ITenantSeeder
{
    public const string AdministratorKey = "administrator";

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
                PasswordHash = hash,
            };
            db.Users.Add(user);
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
            new("password_hash", NpgsqlDbType.Varchar),
            new("is_active", NpgsqlDbType.Boolean),
            new("failed_sign_in_count", NpgsqlDbType.Integer),
            new("created_at", NpgsqlDbType.TimestampTz),
            new("updated_at", NpgsqlDbType.TimestampTz),
        };
        var tenantId = context.Tenant.Id;
        var start = new DateTimeOffset(2024, 1, 1, 6, 0, 0, TimeSpan.Zero);
        var rows = DemoPeople.Generate(context.Tenant.Code, context.Tenant.Volume).Select((p, i) =>
        {
            var email = $"{p.EmailLocal}.{i + 1}@{context.Tenant.EmailDomain}";
            var created = start.AddMinutes(i * 7L);
            return new object?[]
            {
                Guid.CreateVersion7(created), tenantId, email, email.ToLowerInvariant(), context.Mark(p.DisplayName),
                p.Language, hash, i % 23 != 0, 0, created, created,
            };
        });
        await BulkInsert.InsertAsync(session, IdentityDbContext.SchemaName, "users", columns, rows, cancellationToken);
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
