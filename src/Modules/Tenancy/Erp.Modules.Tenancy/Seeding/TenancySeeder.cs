using System.Security.Cryptography;
using System.Text;
using Erp.Kernel.Seeding;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Seeding;

/// <summary>
/// Seeds the tenant row, then its companies and branches: realistic UAE trading and manufacturing
/// groups for the demo, one company and branch for a minimal or newly provisioned workspace, and
/// two companies with two branches each (every text carrying the tenant's canary) for the gate.
/// Ids are time-ordered in creation order, so a company's first branch sorts before the next
/// company's branches.
/// </summary>
internal sealed class TenancySeeder(TenancyDbContext db) : ITenantSeeder
{
    public int Order => 0;

    public async Task SeedAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (!await db.Tenants.AnyAsync(cancellationToken))
        {
            db.Tenants.Add(new Tenant
            {
                Id = context.Tenant.Id,
                Code = context.Tenant.Code,
                NameEn = context.Tenant.NameEn,
                NameAr = context.Tenant.NameAr,
                DefaultLanguage = context.Tenant.Administrator?.Language == "ar" ? "ar" : "en",
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        if (await db.Companies.AnyAsync(cancellationToken))
        {
            return;
        }

        var clock = new DateTimeOffset(2024, 1, 1, 5, 0, 0, TimeSpan.Zero).AddDays(StableOffset(context.Tenant.Code));
        Guid NextId()
        {
            clock = clock.AddMilliseconds(1);
            return Guid.CreateVersion7(clock);
        }

        foreach (var plan in DemoCompanies.For(context))
        {
            var company = plan.Company;
            company.Id = NextId();
            company.CompanyId = company.Id;
            if (context.Plan.Profile == SeedProfile.Gate)
            {
                // A logo whose bytes carry the canary: a logo served to the wrong tenant is visible.
                var png = TinyPng.Create(context.Mark($"logo {company.Code}"));
                company.Logo = png;
                company.LogoContentType = "image/png";
                company.LogoHash = Companies.CompanyLogo.Hash(company.Id, png);
            }
            db.Companies.Add(company);
            foreach (var branch in plan.Branches)
            {
                branch.Id = NextId();
                branch.CompanyId = company.Id;
                db.Branches.Add(branch);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static int StableOffset(string code)
    {
        var sum = 0;
        foreach (var c in code)
        {
            sum = (sum * 31 + c) % 300;
        }
        return sum;
    }
}

internal sealed record CompanyPlan(Company Company, IReadOnlyList<Branch> Branches);

/// <summary>The companies and branches each seed profile starts with.</summary>
internal static class DemoCompanies
{
    public static IReadOnlyList<CompanyPlan> For(TenantSeedContext context)
    {
        var tenant = context.Tenant;
        return context.Plan.Profile switch
        {
            SeedProfile.Demo when tenant.Code == "alnoor" => AlNoor(),
            SeedProfile.Demo => GulfSteel(),
            SeedProfile.Gate => Gate(context),
            _ => [Single(context)],
        };
    }

    private static Company Company(string code, string en, string ar, string city, string? emirate, string address, string addressAr,
        string poBox, string phone, string licence, string authority, string? trn, int fiscalMonth = 1) => new()
    {
        Code = code,
        LegalNameEn = en,
        LegalNameAr = ar,
        TradeLicenceNumber = licence,
        TradeLicenceAuthority = authority,
        TaxRegistrationNumber = trn,
        BaseCurrency = "AED",
        FiscalYearStartMonth = fiscalMonth,
        FiscalYearStartDay = 1,
        AddressLine1 = address,
        City = city,
        Emirate = emirate,
        PoBox = poBox,
        Country = "AE",
        AddressAr = addressAr,
        Phone = phone,
        Email = $"accounts.{code.ToLowerInvariant()}@example.ae",
        Website = "https://www.example.ae",
    };

    private static Branch Branch(string code, string en, string ar, string city, string? emirate, string address, string phone) => new()
    {
        Code = code,
        NameEn = en,
        NameAr = ar,
        City = city,
        Emirate = emirate,
        AddressLine1 = address,
        Country = "AE",
        Phone = phone,
    };

    private static IReadOnlyList<CompanyPlan> AlNoor() =>
    [
        new(Company("ALN-DXB", "Al Noor Trading LLC", "شركة النور للتجارة ذ.م.م", "Dubai", "dubai", "Office 1204, Al Maktoum Road, Deira",
                "مكتب 1204، شارع آل مكتوم، ديرة، دبي", "11542", "+971 4 221 5500", "DED-604112", "Dubai Department of Economy and Tourism", "100123456700003"),
            [
                Branch("DEIRA-HQ", "Deira head office", "المكتب الرئيسي - ديرة", "Dubai", "dubai", "Al Maktoum Road, Deira", "+971 4 221 5500"),
                Branch("AQZ-WH", "Al Quoz warehouse", "مستودع القوز", "Dubai", "dubai", "Street 18, Al Quoz Industrial Area 3", "+971 4 347 2210"),
                Branch("DIP-SR", "Dubai Investments Park showroom", "صالة عرض مجمع دبي للاستثمار", "Dubai", "dubai", "Building 7, Dubai Investments Park 1", "+971 4 885 1290"),
            ]),
        new(Company("ALN-FZE", "Al Noor General Trading FZE", "النور للتجارة العامة م.م.ح", "Dubai", "dubai", "Plot S20311, Jebel Ali Free Zone South",
                "قطعة S20311، المنطقة الحرة لجبل علي جنوب، دبي", "263711", "+971 4 880 4410", "JAFZA-178204", "Jebel Ali Free Zone Authority", "100123456800003"),
            [
                Branch("JAFZA-WH", "JAFZA South warehouse", "مستودع جافزا جنوب", "Dubai", "dubai", "Plot S20311, Jebel Ali Free Zone South", "+971 4 880 4410"),
                Branch("DAFZ-OF", "Dubai Airport Freezone office", "مكتب المنطقة الحرة لمطار دبي", "Dubai", "dubai", "Building 6W, Dubai Airport Freezone", "+971 4 260 3355"),
            ]),
        new(Company("ALN-SHJ", "Al Noor Industries LLC", "مصانع النور ذ.م.م", "Sharjah", "sharjah", "Industrial Area 6, Al Wahda Street",
                "المنطقة الصناعية 6، شارع الوحدة، الشارقة", "26117", "+971 6 543 2100", "SEDD-743920", "Sharjah Economic Development Department", "100123456900003", fiscalMonth: 4),
            [
                Branch("SHJ-FAC", "Sharjah factory", "مصنع الشارقة", "Sharjah", "sharjah", "Industrial Area 6", "+971 6 543 2100"),
                Branch("SAIF-WH", "SAIF Zone store", "مخزن المنطقة الحرة لمطار الشارقة", "Sharjah", "sharjah", "Q4-112, SAIF Zone", "+971 6 557 0180"),
                Branch("AJM-WS", "Ajman workshop", "ورشة عجمان", "Ajman", "ajman", "Al Jurf Industrial Area 2", "+971 6 748 3300"),
            ]),
        new(Company("ALN-AUH", "Al Noor Technical Services LLC", "النور للخدمات الفنية ذ.م.م", "Abu Dhabi", "abuDhabi", "M-37, Mussafah Industrial Area",
                "م-37، المنطقة الصناعية مصفح، أبوظبي", "47721", "+971 2 555 7810", "ADDED-CN-3318204", "Abu Dhabi Department of Economic Development", null),
            [
                Branch("MUS-WS", "Mussafah workshop", "ورشة مصفح", "Abu Dhabi", "abuDhabi", "M-37, Mussafah Industrial Area", "+971 2 555 7810"),
                Branch("AIN-OF", "Al Ain office", "مكتب العين", "Al Ain", "abuDhabi", "Khalifa Street, Al Ain", "+971 3 765 2290"),
                Branch("RAK-ST", "Ras Al Khaimah store", "متجر رأس الخيمة", "Ras Al Khaimah", "rasAlKhaimah", "Al Hamra Industrial Zone", "+971 7 244 6120"),
                Branch("FUJ-ST", "Fujairah store", "متجر الفجيرة", "Fujairah", "fujairah", "Hamad Bin Abdullah Road", "+971 9 222 8040"),
            ]),
    ];

    private static IReadOnlyList<CompanyPlan> GulfSteel() =>
    [
        new(Company("GSF-SHJ", "Gulf Steel Fabrication LLC", "الخليج لتصنيع الصلب ذ.م.م", "Sharjah", "sharjah", "Industrial Area 13",
                "المنطقة الصناعية 13، الشارقة", "39012", "+971 6 534 8800", "SEDD-512230", "Sharjah Economic Development Department", "100765432100003"),
            [
                Branch("SHJ-PLANT", "Sharjah plant", "مصنع الشارقة", "Sharjah", "sharjah", "Industrial Area 13", "+971 6 534 8800"),
                Branch("HAMR-YD", "Hamriyah yard", "ساحة الحمرية", "Sharjah", "sharjah", "Hamriyah Free Zone, Phase 2", "+971 6 526 0110"),
            ]),
        new(Company("GSF-RAK", "Gulf Steel Fabrication FZ-LLC", "الخليج لتصنيع الصلب م.م.ح - ذ.م.م", "Ras Al Khaimah", "rasAlKhaimah", "Al Ghail Industrial Zone",
                "منطقة الغيل الصناعية، رأس الخيمة", "86420", "+971 7 243 5500", "RAKEZ-4410923", "RAK Economic Zone", null),
            [
                Branch("GHAIL-PL", "Al Ghail plant", "مصنع الغيل", "Ras Al Khaimah", "rasAlKhaimah", "Al Ghail Industrial Zone", "+971 7 243 5500"),
            ]),
    ];

    /// <summary>Two companies of two branches each; every text carries the tenant's canary.</summary>
    private static IReadOnlyList<CompanyPlan> Gate(TenantSeedContext context)
    {
        var prefix = context.Tenant.Canary is null ? "A" : "B";
        var name = context.Tenant.NameEn.Split(' ')[0];
        CompanyPlan Make(int n, string en, string ar) => new(
            Company($"{prefix}{n}-CO", context.Mark(en), context.Mark(ar), context.Mark("Dubai"), "dubai", context.Mark($"Street {n}"),
                context.Mark($"شارع {n}"), $"{n}000{n}", $"+971 4 000 000{n}", context.Mark($"LIC-{n}"), context.Mark("Licensing authority"), $"10000000000{n}003"),
            [
                Branch($"{prefix}{n}-BR1", context.Mark($"{en} branch one"), context.Mark($"{ar} فرع أول"), context.Mark("Dubai"), "dubai", context.Mark($"Road {n}1"), $"+971 4 000 00{n}1"),
                Branch($"{prefix}{n}-BR2", context.Mark($"{en} branch two"), context.Mark($"{ar} فرع ثان"), context.Mark("Sharjah"), "sharjah", context.Mark($"Road {n}2"), $"+971 6 000 00{n}2"),
            ]);
        return
        [
            Make(1, $"{name} Trading", "التجارة"),
            Make(2, $"{name} Steel Industries", "الصناعات الفولاذية"),
        ];
    }

    /// <summary>A minimal or newly provisioned workspace: one company named like the workspace
    /// and its head office.</summary>
    private static CompanyPlan Single(TenantSeedContext context)
    {
        var tenant = context.Tenant;
        var code = new string(tenant.Code.ToUpperInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c == '-').Take(20).ToArray());
        if (code.Length < 2 || !char.IsAsciiLetterOrDigit(code[0]))
        {
            code = "HQ-CO";
        }
        return new CompanyPlan(
            new Company { Code = code, LegalNameEn = tenant.NameEn, LegalNameAr = tenant.NameAr, BaseCurrency = "AED", Country = "AE" },
            [new Branch { Code = "HQ", NameEn = "Head office", NameAr = "المكتب الرئيسي", Country = "AE" }]);
    }
}

/// <summary>A valid 1×1 PNG with a text comment (used to make seeded logos recognisable).</summary>
internal static class TinyPng
{
    public static byte[] Create(string comment)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(stream, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
        Chunk(stream, "tEXt", [.. Encoding.Latin1.GetBytes("Comment"), 0, .. Encoding.UTF8.GetBytes(comment)]);
        // zlib stream of one scanline (filter 0, one transparent RGBA pixel) in a stored block.
        Chunk(stream, "IDAT", [0x78, 0x01, 0x01, 0x05, 0x00, 0xFA, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x01]);
        Chunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        WriteUInt32(stream, (uint)data.Length);
        stream.Write(typeBytes);
        stream.Write(data);
        WriteUInt32(stream, Crc32([.. typeBytes, .. data]));
    }

    private static void WriteUInt32(Stream stream, uint value) =>
        stream.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }
}
