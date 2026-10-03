using System.ComponentModel.DataAnnotations;
using Validator = Erp.Kernel.Http.Validator;
using System.Security.Cryptography;
using Erp.Kernel.Data;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Companies;

/// <summary>A company as the company form shows it.</summary>
public sealed record CompanyDto(
    Guid Id,
    string Code,
    string LegalNameEn,
    string LegalNameAr,
    string? TradeLicenceNumber,
    string? TradeLicenceAuthority,
    string? TaxRegistrationNumber,
    string BaseCurrency,
    int FiscalYearStartMonth,
    int FiscalYearStartDay,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    Emirate? Emirate,
    string? PoBox,
    string Country,
    string? AddressAr,
    string? Phone,
    string? Email,
    string? Website,
    bool HasLogo,
    string? LogoHash,
    bool IsActive,
    int BranchCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

/// <summary>A row of the companies list.</summary>
public sealed record CompanyRow(
    Guid Id,
    string Code,
    string LegalNameEn,
    string LegalNameAr,
    string BaseCurrency,
    string? City,
    Emirate? Emirate,
    int BranchCount,
    bool IsActive,
    uint Version);

public sealed record CompanyPage(IReadOnlyList<CompanyRow> Items, int Total);

/// <summary>Create or change a company. The code may be left empty (one is made from the English
/// name); a legal name in English or Arabic is required (both are recommended). On change,
/// <c>version</c> is the version that was read.</summary>
public sealed record SaveCompanyRequest(
    [property: ApiExample("AN-DXB"), StringLength(20, MinimumLength = 2), RegularExpression(TenancyValidation.CodePattern)] string? Code,
    [property: StringLength(200)] string? LegalNameEn,
    [property: StringLength(200)] string? LegalNameAr,
    [property: StringLength(50)] string? TradeLicenceNumber,
    [property: StringLength(100)] string? TradeLicenceAuthority,
    [property: ApiExample("100123456700003"), RegularExpression(TenancyValidation.TaxNumberPattern)] string? TaxRegistrationNumber,
    [property: ApiExample("AED"), RegularExpression("^[A-Z]{3}$")] string? BaseCurrency,
    [property: Range(1, 12)] int? FiscalYearStartMonth,
    [property: Range(1, 31)] int? FiscalYearStartDay,
    [property: StringLength(200)] string? AddressLine1,
    [property: StringLength(200)] string? AddressLine2,
    [property: StringLength(100)] string? City,
    Emirate? Emirate,
    [property: ApiExample("12345"), RegularExpression(TenancyValidation.PoBoxPattern)] string? PoBox,
    [property: ApiExample("AE"), RegularExpression("^[A-Z]{2}$")] string? Country,
    [property: StringLength(400)] string? AddressAr,
    [property: ApiExample("+971 4 123 4567"), RegularExpression(TenancyValidation.PhonePattern)] string? Phone,
    [property: ApiExample("accounts@example.ae"), StringLength(254)] string? Email,
    [property: ApiExample("https://www.example.ae"), RegularExpression(TenancyValidation.WebsitePattern)] string? Website,
    bool? IsActive,
    uint? Version);

/// <summary>A company logo: PNG, JPEG or WebP, at most 512 KB, as base64.</summary>
public sealed record UploadLogoRequest(
    [property: ApiExample("image/png"), RegularExpression("^image/(png|jpeg|webp)$")] string? ContentType,
    [property: ApiExample(CompanyLogo.ExamplePng)] string? Data);

internal static class CompanyEndpoints
{
    public const int MaxPageSize = 200;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/companies", List)
            .WithName("tenancy.companies.list")
            .WithSummary("Companies the caller may work in, by code; search matches code and legal names.")
            .RequirePermission(TenancyPermissions.CompaniesRead);

        group.MapGet("/companies/{id:guid}", Get)
            .WithName("tenancy.companies.get")
            .WithSummary("One company the caller may work in.")
            .RequirePermission(TenancyPermissions.CompaniesRead);

        group.MapPost("/companies", Create)
            .WithName("tenancy.companies.create")
            .WithSummary("Create a company. The creator may work in it (all branches) from then on.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(TenancyPermissions.CompaniesCreate);

        group.MapPut("/companies/{id:guid}", Update)
            .WithName("tenancy.companies.update")
            .WithSummary("Change a company the caller may work in, or deactivate it (isActive false).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(TenancyPermissions.CompaniesUpdate);

        group.MapGet("/companies/{id:guid}/logo", GetLogo)
            .WithName("tenancy.companies.logo.get")
            .WithSummary("The company's logo image (PNG, JPEG or WebP).")
            .Produces(StatusCodes.Status200OK, contentType: "image/png", additionalContentTypes: ["image/jpeg", "image/webp"])
            .Surface(SurfaceKind.File)
            .RequirePermission(TenancyPermissions.CompaniesRead);

        group.MapPut("/companies/{id:guid}/logo", PutLogo)
            .WithName("tenancy.companies.logo.put")
            .WithSummary("Replace the company's logo: PNG, JPEG or WebP, at most 512 KB, base64 in data.")
            .ProducesValidationProblem()
            .Surface(SurfaceKind.File)
            .RequirePermission(TenancyPermissions.CompaniesUpdate);

        group.MapDelete("/companies/{id:guid}/logo", DeleteLogo)
            .WithName("tenancy.companies.logo.delete")
            .WithSummary("Remove the company's logo.")
            .Surface(SurfaceKind.File)
            .RequirePermission(TenancyPermissions.CompaniesUpdate);
    }

    private static async Task<Ok<CompanyPage>> List(
        TenancyDbContext db, string? search, bool? isActive, string? sort, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = db.Companies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + Like.Escape(search.Trim()) + "%";
            query = query.Where(c => EF.Functions.ILike(c.Code, pattern, "\\") || EF.Functions.ILike(c.LegalNameEn, pattern, "\\")
                                     || EF.Functions.ILike(c.LegalNameAr, pattern, "\\"));
        }
        if (isActive is { } active)
        {
            query = query.Where(c => c.IsActive == active);
        }
        var total = await query.CountAsync(cancellationToken);
        var ordered = (sort ?? "code") switch
        {
            "-code" => query.OrderByDescending(c => c.Code),
            "legalNameEn" => query.OrderBy(c => c.LegalNameEn),
            "-legalNameEn" => query.OrderByDescending(c => c.LegalNameEn),
            "legalNameAr" => query.OrderBy(c => c.LegalNameAr),
            "-legalNameAr" => query.OrderByDescending(c => c.LegalNameAr),
            "city" => query.OrderBy(c => c.City),
            "-city" => query.OrderByDescending(c => c.City),
            "branchCount" => query.OrderBy(c => db.Branches.Count(b => b.CompanyId == c.Id)),
            "-branchCount" => query.OrderByDescending(c => db.Branches.Count(b => b.CompanyId == c.Id)),
            _ => query.OrderBy(c => c.Code),
        };
        var rows = await ordered.ThenBy(c => c.Id)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .Select(c => new
            {
                c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.BaseCurrency, c.City, c.Emirate, c.IsActive, c.Version,
                Branches = db.Branches.Count(b => b.CompanyId == c.Id),
            })
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new CompanyPage(
            rows.Select(r => new CompanyRow(r.Id, r.Code, r.LegalNameEn, r.LegalNameAr, r.BaseCurrency, r.City,
                TenancyValidation.ParseEmirate(r.Emirate), r.Branches, r.IsActive, r.Version)).ToList(),
            total));
    }

    private static async Task<Results<Ok<CompanyDto>, ProblemHttpResult>> Get(Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(await ToDtoAsync(db, company, cancellationToken));
    }

    private static async Task<Results<Created<CompanyDto>, ProblemHttpResult>> Create(
        SaveCompanyRequest request, TenancyDbContext db, ErpDbSession session, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, http, requireVersion: false);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var code = TenancyValidation.NormalizeCode(request.Code);
        if (string.IsNullOrEmpty(code))
        {
            // No code typed: one is made from the name, unique among the companies in sight.
            var taken = await db.Companies.AsNoTracking().Select(c => c.Code).ToListAsync(cancellationToken);
            code = TenancyValidation.SuggestCode(request.LegalNameEn, taken, "CO");
        }
        else if (await db.Companies.AnyAsync(c => c.Code == code, cancellationToken))
        {
            return Problems.Conflict(http, "tenancy.companyCodeTaken");
        }
        var company = new Company();
        company.CompanyId = company.Id;
        Apply(company, request, code);
        // The creator works in the new company from now on; it joins this request's scope so the
        // company and the creator's access to it can be written.
        await session.IncludeNewCompanyAsync(company.Id, cancellationToken);
        db.Companies.Add(company);
        db.CompanyAccess.Add(new UserCompanyAccess { UserId = caller.UserId, CompanyId = company.Id, AllBranches = true });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/tenancy/companies/{company.Id}", await ToDtoAsync(db, company, cancellationToken));
    }

    private static async Task<Results<Ok<CompanyDto>, ProblemHttpResult>> Update(
        Guid id, SaveCompanyRequest request, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, http, requireVersion: true);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var company = await db.Companies.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null)
        {
            return Problems.NotFound(http);
        }
        var code = TenancyValidation.NormalizeCode(request.Code) is { Length: > 0 } typed ? typed : company.Code;
        if (code != company.Code && await db.Companies.AnyAsync(c => c.Code == code && c.Id != id, cancellationToken))
        {
            return Problems.Conflict(http, "tenancy.companyCodeTaken");
        }
        db.Entry(company).Property(c => c.Version).OriginalValue = request.Version!.Value;
        Apply(company, request, code);
        db.Entry(company).Property(c => c.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToDtoAsync(db, company, cancellationToken));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> GetLogo(
        Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var logo = await db.Companies.AsNoTracking().Where(c => c.Id == id && c.Logo != null)
            .Select(c => new { c.Logo, c.LogoContentType, c.LogoHash })
            .SingleOrDefaultAsync(cancellationToken);
        if (logo is null)
        {
            return Problems.NotFound(http);
        }
        http.Response.Headers.ContentDisposition = "inline";
        return TypedResults.File(logo.Logo!, logo.LogoContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{logo.LogoHash}\""));
    }

    private static async Task<Results<Ok<CompanyDto>, ProblemHttpResult>> PutLogo(
        Guid id, UploadLogoRequest request, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("contentType", request.ContentType)
            .OneOf("contentType", request.ContentType, CompanyLogo.ContentTypes)
            .Required("data", request.Data);
        byte[]? bytes = null;
        if (!string.IsNullOrWhiteSpace(request.Data))
        {
            bytes = CompanyLogo.Decode(request.Data);
            validator.Must(bytes is not null, "data", "tenancyLogoEncoding")
                .Must(bytes is null || bytes.Length <= CompanyLogo.MaxBytes, "data", "tenancyLogoTooLarge", CompanyLogo.MaxBytes / 1024)
                .Must(bytes is null || request.ContentType is null || CompanyLogo.Matches(bytes, request.ContentType), "data", "tenancyLogoType");
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var company = await db.Companies.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null)
        {
            return Problems.NotFound(http);
        }
        company.Logo = bytes;
        company.LogoContentType = request.ContentType;
        company.LogoHash = Convert.ToHexStringLower(SHA256.HashData(bytes!));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToDtoAsync(db, company, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteLogo(Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var company = await db.Companies.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (company is null)
        {
            return Problems.NotFound(http);
        }
        if (company.Logo is not null)
        {
            company.Logo = null;
            company.LogoContentType = null;
            company.LogoHash = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        return TypedResults.NoContent();
    }

    private static Validator Validate(SaveCompanyRequest request, HttpContext http, bool requireVersion)
    {
        var validator = new Validator(http)
            .Code("code", TenancyValidation.NormalizeCode(request.Code))
            .Names("legalNameEn", request.LegalNameEn, "legalNameAr", request.LegalNameAr)
            .MaxLength("tradeLicenceNumber", request.TradeLicenceNumber, 50)
            .MaxLength("tradeLicenceAuthority", request.TradeLicenceAuthority, 100)
            .TaxNumber(request.TaxRegistrationNumber)
            .Currency(TenancyValidation.Clean(request.BaseCurrency)?.ToUpperInvariant())
            .FiscalYearStart(request.FiscalYearStartMonth, request.FiscalYearStartDay)
            .Address(AddressOf(request))
            .Website(request.Website)
            .Required("isActive", request.IsActive);
        if (requireVersion)
        {
            validator.Required("version", request.Version);
        }
        return validator;
    }

    private static AddressFields AddressOf(SaveCompanyRequest r) =>
        new(r.AddressLine1, r.AddressLine2, r.City, r.Emirate, r.PoBox, TenancyValidation.Clean(r.Country)?.ToUpperInvariant(), r.AddressAr, r.Phone, r.Email);

    private static void Apply(Company company, SaveCompanyRequest r, string code)
    {
        company.Code = code;
        company.LegalNameEn = r.LegalNameEn?.Trim() ?? "";
        company.LegalNameAr = r.LegalNameAr?.Trim() ?? "";
        company.TradeLicenceNumber = TenancyValidation.Clean(r.TradeLicenceNumber);
        company.TradeLicenceAuthority = TenancyValidation.Clean(r.TradeLicenceAuthority);
        company.TaxRegistrationNumber = TenancyValidation.Clean(r.TaxRegistrationNumber);
        company.BaseCurrency = r.BaseCurrency!.Trim().ToUpperInvariant();
        company.FiscalYearStartMonth = r.FiscalYearStartMonth!.Value;
        company.FiscalYearStartDay = r.FiscalYearStartDay!.Value;
        company.AddressLine1 = TenancyValidation.Clean(r.AddressLine1);
        company.AddressLine2 = TenancyValidation.Clean(r.AddressLine2);
        company.City = TenancyValidation.Clean(r.City);
        company.Emirate = TenancyValidation.EmirateValue(r.Emirate);
        company.PoBox = TenancyValidation.Clean(r.PoBox);
        company.Country = r.Country!.Trim().ToUpperInvariant();
        company.AddressAr = TenancyValidation.Clean(r.AddressAr);
        company.Phone = TenancyValidation.Clean(r.Phone);
        company.Email = TenancyValidation.Clean(r.Email);
        company.Website = TenancyValidation.Clean(r.Website);
        company.IsActive = r.IsActive!.Value;
    }

    internal static async Task<CompanyDto> ToDtoAsync(TenancyDbContext db, Company c, CancellationToken cancellationToken)
    {
        var branches = await db.Branches.CountAsync(b => b.CompanyId == c.Id, cancellationToken);
        return new CompanyDto(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr, c.TradeLicenceNumber, c.TradeLicenceAuthority, c.TaxRegistrationNumber,
            c.BaseCurrency, c.FiscalYearStartMonth, c.FiscalYearStartDay, c.AddressLine1, c.AddressLine2, c.City,
            TenancyValidation.ParseEmirate(c.Emirate), c.PoBox, c.Country, c.AddressAr, c.Phone, c.Email, c.Website,
            c.Logo is not null || c.LogoHash is not null, c.LogoHash, c.IsActive, branches, c.CreatedAt, c.UpdatedAt, c.Version);
    }
}

/// <summary>Logo uploads: only raster images whose bytes match their declared type.</summary>
internal static class CompanyLogo
{
    public const int MaxBytes = 512 * 1024;

    public static readonly IReadOnlyList<string> ContentTypes = ["image/png", "image/jpeg", "image/webp"];

    /// <summary>A valid 1×1 PNG (the documented example).</summary>
    public const string ExamplePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public static byte[]? Decode(string data)
    {
        var text = data.Trim();
        var comma = text.IndexOf(',', StringComparison.Ordinal);
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            text = text[(comma + 1)..];
        }
        if (text.Length > (MaxBytes + 3) / 3 * 4 + 16)
        {
            // Too large to be a valid logo; report it as too large without decoding it.
            return new byte[MaxBytes + 1];
        }
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static bool Matches(byte[] bytes, string contentType) => contentType switch
    {
        "image/png" => bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        "image/jpeg" => bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/webp" => bytes.Length > 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
        _ => false,
    };
}

/// <summary>ILIKE patterns that match user text literally.</summary>
internal static class Like
{
    public static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
