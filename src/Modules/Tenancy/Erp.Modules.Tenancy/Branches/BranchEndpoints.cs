using System.ComponentModel.DataAnnotations;
using Validator = Erp.Kernel.Http.Validator;
using Erp.Kernel.Hosting;
using Erp.Kernel.Http;
using Erp.Kernel.Security;
using Erp.Modules.Tenancy.Companies;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Tenancy.Branches;

public sealed record BranchDto(
    Guid Id,
    Guid CompanyId,
    string CompanyCode,
    string Code,
    string NameEn,
    string NameAr,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    Emirate? Emirate,
    string? PoBox,
    string Country,
    string? AddressAr,
    string? Phone,
    string? Email,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version);

public sealed record BranchRow(
    Guid Id,
    Guid CompanyId,
    string CompanyCode,
    string Code,
    string NameEn,
    string NameAr,
    string? City,
    Emirate? Emirate,
    bool IsActive,
    uint Version);

public sealed record BranchPage(IReadOnlyList<BranchRow> Items, int Total);

/// <summary>Create or change a branch. A branch stays in the company it was created in. The code
/// may be left empty (one is made from the English name); a name in English or Arabic is required.</summary>
public sealed record SaveBranchRequest(
    Guid? CompanyId,
    [property: ApiExample("DXB-WH1"), StringLength(20, MinimumLength = 2), RegularExpression(TenancyValidation.CodePattern)] string? Code,
    [property: StringLength(200)] string? NameEn,
    [property: StringLength(200)] string? NameAr,
    [property: StringLength(200)] string? AddressLine1,
    [property: StringLength(200)] string? AddressLine2,
    [property: StringLength(100)] string? City,
    Emirate? Emirate,
    [property: ApiExample("12345"), RegularExpression(TenancyValidation.PoBoxPattern)] string? PoBox,
    [property: ApiExample("AE"), RegularExpression("^[A-Z]{2}$")] string? Country,
    [property: StringLength(400)] string? AddressAr,
    [property: ApiExample("+971 4 123 4567"), RegularExpression(TenancyValidation.PhonePattern)] string? Phone,
    [property: ApiExample("branch@example.ae"), StringLength(254)] string? Email,
    bool? IsActive,
    uint? Version);

internal static class BranchEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/branches", List)
            .WithName("tenancy.branches.list")
            .WithSummary("Branches of the companies the caller may work in, by company and code; optionally of one company.")
            .RequirePermission(TenancyPermissions.BranchesRead);

        group.MapGet("/branches/{id:guid}", Get)
            .WithName("tenancy.branches.get")
            .WithSummary("One branch of a company the caller may work in.")
            .RequirePermission(TenancyPermissions.BranchesRead);

        group.MapPost("/branches", Create)
            .WithName("tenancy.branches.create")
            .WithSummary("Create a branch of an active company the caller may work in.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(TenancyPermissions.BranchesCreate);

        group.MapPut("/branches/{id:guid}", Update)
            .WithName("tenancy.branches.update")
            .WithSummary("Change a branch, or deactivate it (isActive false).")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(TenancyPermissions.BranchesUpdate);
    }

    private static async Task<Ok<BranchPage>> List(TenancyDbContext db, Guid? companyId, string? search, bool? isActive, string? sort,
        int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = from b in db.Branches.AsNoTracking()
                    join c in db.Companies.AsNoTracking() on b.CompanyId equals c.Id
                    select new { Branch = b, CompanyCode = c.Code };
        if (companyId is { } company)
        {
            query = query.Where(x => x.Branch.CompanyId == company);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + Like.Escape(search.Trim()) + "%";
            query = query.Where(x => EF.Functions.ILike(x.Branch.Code, pattern, "\\") || EF.Functions.ILike(x.Branch.NameEn, pattern, "\\")
                                     || EF.Functions.ILike(x.Branch.NameAr, pattern, "\\"));
        }
        if (isActive is { } active)
        {
            query = query.Where(x => x.Branch.IsActive == active);
        }
        var total = await query.CountAsync(cancellationToken);
        var ordered = (sort ?? "code") switch
        {
            "-code" => query.OrderByDescending(x => x.CompanyCode).ThenByDescending(x => x.Branch.Code),
            "nameEn" => query.OrderBy(x => x.Branch.NameEn),
            "-nameEn" => query.OrderByDescending(x => x.Branch.NameEn),
            "nameAr" => query.OrderBy(x => x.Branch.NameAr),
            "-nameAr" => query.OrderByDescending(x => x.Branch.NameAr),
            "city" => query.OrderBy(x => x.Branch.City),
            "-city" => query.OrderByDescending(x => x.Branch.City),
            _ => query.OrderBy(x => x.CompanyCode).ThenBy(x => x.Branch.Code),
        };
        var rows = await ordered.ThenBy(x => x.Branch.Id)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, CompanyEndpoints.MaxPageSize))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new BranchPage(
            rows.Select(x => new BranchRow(x.Branch.Id, x.Branch.CompanyId, x.CompanyCode, x.Branch.Code, x.Branch.NameEn, x.Branch.NameAr,
                x.Branch.City, TenancyValidation.ParseEmirate(x.Branch.Emirate), x.Branch.IsActive, x.Branch.Version)).ToList(),
            total));
    }

    private static async Task<Results<Ok<BranchDto>, ProblemHttpResult>> Get(Guid id, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(await ToDtoAsync(db, branch, cancellationToken));
    }

    private static async Task<Results<Created<BranchDto>, ProblemHttpResult>> Create(
        SaveBranchRequest request, TenancyDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, http, requireVersion: false);
        var company = request.CompanyId is { } companyId
            ? await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => new { c.Id, c.IsActive }).SingleOrDefaultAsync(cancellationToken)
            : null;
        validator.Must(request.CompanyId is null || company is not null, "companyId", "unknownIds")
            .Must(company is null || company.IsActive, "companyId", "tenancyCompanyInactive");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var code = TenancyValidation.NormalizeCode(request.Code);
        if (string.IsNullOrEmpty(code))
        {
            var taken = await db.Branches.AsNoTracking().Where(b => b.CompanyId == company!.Id).Select(b => b.Code).ToListAsync(cancellationToken);
            code = TenancyValidation.SuggestCode(request.NameEn, taken, "BR");
        }
        else if (await db.Branches.AnyAsync(b => b.CompanyId == company!.Id && b.Code == code, cancellationToken))
        {
            return Problems.Conflict(http, "tenancy.branchCodeTaken");
        }
        var branch = new Branch { CompanyId = company!.Id };
        Apply(branch, request, code);
        db.Branches.Add(branch);
        // A creator limited to some branches of this company may work in the branch they created.
        var own = await db.CompanyAccess.SingleOrDefaultAsync(a => a.UserId == caller.UserId && a.CompanyId == company.Id, cancellationToken);
        if (own is { AllBranches: false })
        {
            db.BranchAccess.Add(new UserBranchAccess { UserId = caller.UserId, CompanyId = company.Id, BranchId = branch.Id });
        }
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/tenancy/branches/{branch.Id}", await ToDtoAsync(db, branch, cancellationToken));
    }

    private static async Task<Results<Ok<BranchDto>, ProblemHttpResult>> Update(
        Guid id, SaveBranchRequest request, TenancyDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, http, requireVersion: true);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
        {
            return Problems.NotFound(http);
        }
        if (request.CompanyId != branch.CompanyId)
        {
            return new Validator(http).Add("companyId", "tenancyBranchCompanyFixed").ToResult();
        }
        var code = TenancyValidation.NormalizeCode(request.Code) is { Length: > 0 } typed ? typed : branch.Code;
        if (code != branch.Code && await db.Branches.AnyAsync(b => b.CompanyId == branch.CompanyId && b.Code == code && b.Id != id, cancellationToken))
        {
            return Problems.Conflict(http, "tenancy.branchCodeTaken");
        }
        db.Entry(branch).Property(b => b.Version).OriginalValue = request.Version!.Value;
        Apply(branch, request, code);
        db.Entry(branch).Property(b => b.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ToDtoAsync(db, branch, cancellationToken));
    }

    private static Validator Validate(SaveBranchRequest request, HttpContext http, bool requireVersion)
    {
        var validator = new Validator(http)
            .Required("companyId", request.CompanyId)
            .Code("code", TenancyValidation.NormalizeCode(request.Code))
            .Names("nameEn", request.NameEn, "nameAr", request.NameAr)
            .Address(new AddressFields(request.AddressLine1, request.AddressLine2, request.City, request.Emirate, request.PoBox,
                TenancyValidation.Clean(request.Country)?.ToUpperInvariant(), request.AddressAr, request.Phone, request.Email))
            .Required("isActive", request.IsActive);
        if (requireVersion)
        {
            validator.Required("version", request.Version);
        }
        return validator;
    }

    private static void Apply(Branch branch, SaveBranchRequest r, string code)
    {
        branch.Code = code;
        branch.NameEn = r.NameEn?.Trim() ?? "";
        branch.NameAr = r.NameAr?.Trim() ?? "";
        branch.AddressLine1 = TenancyValidation.Clean(r.AddressLine1);
        branch.AddressLine2 = TenancyValidation.Clean(r.AddressLine2);
        branch.City = TenancyValidation.Clean(r.City);
        branch.Emirate = TenancyValidation.EmirateValue(r.Emirate);
        branch.PoBox = TenancyValidation.Clean(r.PoBox);
        branch.Country = r.Country!.Trim().ToUpperInvariant();
        branch.AddressAr = TenancyValidation.Clean(r.AddressAr);
        branch.Phone = TenancyValidation.Clean(r.Phone);
        branch.Email = TenancyValidation.Clean(r.Email);
        branch.IsActive = r.IsActive!.Value;
    }

    private static async Task<BranchDto> ToDtoAsync(TenancyDbContext db, Branch b, CancellationToken cancellationToken)
    {
        var companyCode = await db.Companies.AsNoTracking().Where(c => c.Id == b.CompanyId).Select(c => c.Code).SingleAsync(cancellationToken);
        return new BranchDto(b.Id, b.CompanyId, companyCode, b.Code, b.NameEn, b.NameAr, b.AddressLine1, b.AddressLine2, b.City,
            TenancyValidation.ParseEmirate(b.Emirate), b.PoBox, b.Country, b.AddressAr, b.Phone, b.Email, b.IsActive, b.CreatedAt, b.UpdatedAt, b.Version);
    }
}
