using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Roles;

public sealed record RoleDto(Guid Id, string NameEn, string NameAr, IReadOnlyList<string> Permissions, bool IsSystem, int UserCount, uint Version);

public sealed record SaveRoleRequest(string? NameEn, string? NameAr, IReadOnlyList<string>? Permissions, uint? Version);

/// <summary>Name of a copy of a role.</summary>
public sealed record CopyRoleRequest(string? NameEn, string? NameAr);

/// <summary>A permission for the role editor's matrix: rows are a module's resources, columns their
/// actions. <c>ResourceLabel</c> falls back to the permission's own label when the module names
/// no resource label.</summary>
public sealed record PermissionDto(string Key, string Module, string Label, string ModuleLabel, string Resource, string Action, string ResourceLabel);

internal static class RoleEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/roles", List)
            .WithName("identity.roles.list")
            .WithSummary("Roles of the workspace with the permissions they grant and their user counts, under the list query contract (search, filters, sort, paging, grouping); system roles first by default.")
            .RequirePermission(IdentityPermissions.RolesRead);

        group.MapGet("/roles/{id:guid}", Get)
            .WithName("identity.roles.get")
            .WithSummary("One role.")
            .RequirePermission(IdentityPermissions.RolesRead);

        group.MapPost("/roles", Create)
            .WithName("identity.roles.create")
            .WithSummary("Create a role. It may only grant permissions the caller holds.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.RolesCreate);

        group.MapPut("/roles/{id:guid}", Update)
            .WithName("identity.roles.update")
            .WithSummary("Rename a role or change its permissions. Only a caller who holds every permission the role grants, before and after the change; system roles cannot be changed.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.RolesUpdate);

        group.MapDelete("/roles/{id:guid}", Delete)
            .WithName("identity.roles.delete")
            .WithSummary("Delete a role and its assignments. Only a caller who holds every permission the role grants, and not a role the caller holds; system roles cannot be deleted.")
            .RequirePermission(IdentityPermissions.RolesDelete);

        group.MapPost("/roles/{id:guid}/copy", Copy)
            .WithName("identity.roles.copy")
            .WithSummary("Create a new role with the same permissions as this one, under a new name. Only roles whose permissions the caller holds.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.RolesCreate);

        group.MapGet("/permissions", Permissions)
            .WithName("identity.permissions.list")
            .WithSummary("Every permission in the catalogue with its label in the caller's language.")
            .RequirePermission(IdentityPermissions.RolesRead);
    }

    private static async Task<Results<Ok<ListPage<RoleDto>>, ProblemHttpResult>> List(
        IdentityDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await PageAsync(db, catalog, request, http, cancellationToken);
        return result.Problem is { } problem ? problem : TypedResults.Ok(result.ToPage(r => r));
    }

    /// <summary>One page of the roles list exactly as the endpoint serves it (reports print it too).</summary>
    internal static async Task<ListResult<RoleDto>> PageAsync(IdentityDbContext db, ModuleCatalog catalog, ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var counts = await db.UserRoles.AsNoTracking().GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);
        var roles = await db.Roles.AsNoTracking().ToListAsync(cancellationToken);
        var rows = roles.Select(r => ToDto(r, counts.GetValueOrDefault(r.Id))).ToList();
        return await catalog.ListBinding<RoleDto>(RolesList.Key).QueryAsync(rows.AsQueryable(), request, http, cancellationToken);
    }

    private static async Task<Results<Ok<RoleDto>, ProblemHttpResult>> Get(Guid id, IdentityDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Problems.NotFound(http);
        }
        var count = await db.UserRoles.CountAsync(ur => ur.RoleId == id, cancellationToken);
        return TypedResults.Ok(ToDto(role, count));
    }

    private static async Task<Results<Created<RoleDto>, ProblemHttpResult>> Create(
        SaveRoleRequest request, IdentityDbContext db, ModuleCatalog catalog, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, catalog, http, requireVersion: false);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var permissions = request.Permissions!.Distinct().Order(StringComparer.Ordinal).ToList();
        if (!permissions.All(caller.Has))
        {
            return Problems.Forbidden(http, "identity.grantBeyondOwn");
        }
        var nameEn = request.NameEn!.Trim();
        if (await db.Roles.AnyAsync(r => r.NameEn == nameEn, cancellationToken))
        {
            return Problems.Conflict(http, "identity.roleNameTaken");
        }
        var role = new Role { NameEn = nameEn, NameAr = request.NameAr!.Trim(), Permissions = permissions };
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/identity/roles/{role.Id}", ToDto(role, 0));
    }

    private static async Task<Results<Created<RoleDto>, ProblemHttpResult>> Copy(
        Guid id, CopyRoleRequest request, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("nameEn", request.NameEn).MaxLength("nameEn", request.NameEn, 100)
            .Required("nameAr", request.NameAr).MaxLength("nameAr", request.NameAr, 100);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var source = await db.Roles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (source is null)
        {
            return Problems.NotFound(http);
        }
        if (!source.Permissions.All(caller.Has))
        {
            return Problems.Forbidden(http, "identity.roleBeyondOwn");
        }
        var nameEn = request.NameEn!.Trim();
        if (await db.Roles.AnyAsync(r => r.NameEn == nameEn, cancellationToken))
        {
            return Problems.Conflict(http, "identity.roleNameTaken");
        }
        var role = new Role { NameEn = nameEn, NameAr = request.NameAr!.Trim(), Permissions = [.. source.Permissions] };
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/identity/roles/{role.Id}", ToDto(role, 0));
    }

    private static async Task<Results<Ok<RoleDto>, ProblemHttpResult>> Update(
        Guid id, SaveRoleRequest request, IdentityDbContext db, ModuleCatalog catalog, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(request, catalog, http, requireVersion: true);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Problems.NotFound(http);
        }
        if (role.IsSystem)
        {
            return Problems.Forbidden(http, "identity.systemRole");
        }
        var permissions = request.Permissions!.Distinct().Order(StringComparer.Ordinal).ToList();
        // A role is changed only by someone who holds everything it grants now and everything it
        // will grant: changing a role acts on everyone who holds it (renaming the finance role
        // "Leavers" is as harmful as clearing it), and adding or removing a permission the caller
        // does not hold is an escalation either way.
        if (!role.Permissions.All(caller.Has))
        {
            return Problems.Forbidden(http, "identity.roleBeyondOwn");
        }
        if (!permissions.All(caller.Has))
        {
            return Problems.Forbidden(http, "identity.grantBeyondOwn");
        }
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id && ur.UserId == caller.UserId, cancellationToken) &&
            !role.Permissions.ToHashSet().SetEquals(permissions))
        {
            return Problems.Forbidden(http, "identity.cannotChangeOwnAccess");
        }
        var nameEn = request.NameEn!.Trim();
        if (await db.Roles.AnyAsync(r => r.NameEn == nameEn && r.Id != id, cancellationToken))
        {
            return Problems.Conflict(http, "identity.roleNameTaken");
        }
        db.Entry(role).Property(r => r.Version).OriginalValue = request.Version!.Value;
        role.NameEn = nameEn;
        role.NameAr = request.NameAr!.Trim();
        role.Permissions = permissions;
        db.Entry(role).Property(r => r.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        var count = await db.UserRoles.CountAsync(ur => ur.RoleId == id, cancellationToken);
        return TypedResults.Ok(ToDto(role, count));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        Guid id, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Problems.NotFound(http);
        }
        if (role.IsSystem)
        {
            return Problems.Forbidden(http, "identity.systemRole");
        }
        if (!role.Permissions.All(caller.Has))
        {
            return Problems.Forbidden(http, "identity.roleBeyondOwn");
        }
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id && ur.UserId == caller.UserId, cancellationToken))
        {
            return Problems.Forbidden(http, "identity.cannotChangeOwnAccess");
        }
        // Remove assignments explicitly so each removal is audited by the row trigger.
        db.UserRoles.RemoveRange(await db.UserRoles.Where(ur => ur.RoleId == id).ToListAsync(cancellationToken));
        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static Ok<List<PermissionDto>> Permissions(ModuleCatalog catalog, StringCatalog strings, HttpContext http)
    {
        var language = Languages.ForRequest(http);
        return TypedResults.Ok(catalog.Permissions
            .Select(p =>
            {
                var label = strings.Get(p.LabelKey, language);
                var resourceKey = $"resource.{p.Module}.{p.Resource}";
                return new PermissionDto(p.Key, p.Module, label, strings.Get($"module.{p.Module}", language), p.Resource, p.Action,
                    strings.Contains(resourceKey) ? strings.Get(resourceKey, language) : label);
            })
            .OrderBy(p => p.Module, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList());
    }

    private static Validator Validate(SaveRoleRequest request, ModuleCatalog catalog, HttpContext http, bool requireVersion)
    {
        var validator = new Validator(http)
            .Required("nameEn", request.NameEn).MaxLength("nameEn", request.NameEn, 100)
            .Required("nameAr", request.NameAr).MaxLength("nameAr", request.NameAr, 100)
            .Must(request.Permissions is not null, "permissions", "required");
        if (request.Permissions is not null)
        {
            validator.Must(request.Permissions.All(catalog.IsPermission), "permissions", "unknownIds");
        }
        if (requireVersion)
        {
            validator.Required("version", request.Version);
        }
        return validator;
    }

    private static RoleDto ToDto(Role r, int userCount) =>
        new(r.Id, r.NameEn, r.NameAr, r.Permissions, r.IsSystem, userCount, r.Version);
}
