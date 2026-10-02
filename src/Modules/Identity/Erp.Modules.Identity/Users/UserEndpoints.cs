using Erp.Kernel.Http;
using Erp.Kernel.Localization;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Users;

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Language,
    bool IsActive,
    IReadOnlyList<Guid> RoleIds,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record UserPage(IReadOnlyList<UserDto> Items, int Total);

public sealed record CreateUserRequest(string? Email, string? DisplayName, string? Language, string? Password, IReadOnlyList<Guid>? RoleIds);

public sealed record UpdateUserRequest(string? DisplayName, string? Language, bool? IsActive, IReadOnlyList<Guid>? RoleIds, uint? Version);

internal static class UserEndpoints
{
    public const int MaxPageSize = 200;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/users", List)
            .WithName("identity.users.list")
            .WithSummary("Users of the workspace, newest first, optionally filtered by e-mail or name.")
            .RequirePermission(IdentityPermissions.UsersRead);

        group.MapGet("/users/{id:guid}", Get)
            .WithName("identity.users.get")
            .WithSummary("One user.")
            .RequirePermission(IdentityPermissions.UsersRead);

        group.MapPost("/users", Create)
            .WithName("identity.users.create")
            .WithSummary("Create a user with a password and roles. Roles may only grant permissions the caller holds.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersCreate);

        group.MapPut("/users/{id:guid}", Update)
            .WithName("identity.users.update")
            .WithSummary("Change a user's name, language, active flag and roles. Callers cannot change their own roles or deactivate themselves.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersUpdate);
    }

    private static async Task<Ok<UserPage>> List(IdentityDbContext db, string? search, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + EscapeLike(search.Trim().ToLowerInvariant()) + "%";
            query = query.Where(u => EF.Functions.ILike(u.EmailNormalized, pattern, "\\") || EF.Functions.ILike(u.DisplayName, pattern, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        var users = await query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);
        var roles = await RolesOf(db, users.Select(u => u.Id).ToList(), cancellationToken);
        return TypedResults.Ok(new UserPage(users.Select(u => ToDto(u, roles)).ToList(), total));
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> Get(Guid id, IdentityDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(ToDto(user, await RolesOf(db, [user.Id], cancellationToken)));
    }

    private static async Task<Results<Created<UserDto>, ProblemHttpResult>> Create(
        CreateUserRequest request, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();
        var validator = new Validator(http)
            .Required("email", email).Email("email", email)
            .Required("displayName", request.DisplayName).MaxLength("displayName", request.DisplayName, 200)
            .Required("language", request.Language).OneOf("language", request.Language, Languages.All)
            .Required("password", request.Password);
        if (request.Password is { } password)
        {
            validator.Must(password.Length >= PasswordHasher.MinLength, "password", "passwordTooShort", PasswordHasher.MinLength)
                .Must(password.Length <= PasswordHasher.MaxLength, "password", "passwordTooLong", PasswordHasher.MaxLength);
        }
        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToListAsync(cancellationToken);
        validator.Must(roles.Count == roleIds.Count, "roleIds", "unknownIds");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!RoleGrants.WithinCaller(roles, caller))
        {
            return Problems.Forbidden(http, "identity.grantBeyondOwn");
        }
        var normalized = email!.ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.EmailNormalized == normalized, cancellationToken))
        {
            return Problems.Conflict(http, "identity.emailTaken");
        }
        var user = new User
        {
            Email = email,
            EmailNormalized = normalized,
            DisplayName = request.DisplayName!.Trim(),
            Language = request.Language!,
            PasswordHash = PasswordHasher.Hash(request.Password!),
        };
        db.Users.Add(user);
        db.UserRoles.AddRange(roleIds.Select(r => new UserRole { UserId = user.Id, RoleId = r }));
        await db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds });
        return TypedResults.Created($"/api/identity/users/{user.Id}", dto);
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> Update(
        Guid id, UpdateUserRequest request, IdentityDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http)
            .Required("displayName", request.DisplayName).MaxLength("displayName", request.DisplayName, 200)
            .Required("language", request.Language).OneOf("language", request.Language, Languages.All)
            .Required("isActive", request.IsActive)
            .Required("version", request.Version);
        var roleIds = (request.RoleIds ?? []).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToListAsync(cancellationToken);
        validator.Must(roles.Count == roleIds.Count, "roleIds", "unknownIds");
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        var current = await db.UserRoles.Where(ur => ur.UserId == id).ToListAsync(cancellationToken);
        var rolesChanged = !current.Select(c => c.RoleId).ToHashSet().SetEquals(roleIds);
        if (user.Id == caller.UserId && (rolesChanged || request.IsActive == false))
        {
            return Problems.Forbidden(http, "identity.cannotChangeOwnAccess");
        }
        if (rolesChanged)
        {
            // Every role being added or removed must stay within the caller's own permissions.
            var removedIds = current.Select(c => c.RoleId).Except(roleIds).ToList();
            var removed = await db.Roles.AsNoTracking().Where(r => removedIds.Contains(r.Id)).ToListAsync(cancellationToken);
            var added = roles.Where(r => current.All(c => c.RoleId != r.Id)).ToList();
            if (!RoleGrants.WithinCaller(added.Concat(removed), caller))
            {
                return Problems.Forbidden(http, "identity.grantBeyondOwn");
            }
            db.UserRoles.RemoveRange(current.Where(c => !roleIds.Contains(c.RoleId)));
            db.UserRoles.AddRange(roleIds.Where(r => current.All(c => c.RoleId != r)).Select(r => new UserRole { UserId = id, RoleId = r }));
        }
        db.Entry(user).Property(u => u.Version).OriginalValue = request.Version!.Value;
        user.DisplayName = request.DisplayName!.Trim();
        user.Language = request.Language!;
        user.IsActive = request.IsActive!.Value;
        // Force a version check even when only roles changed.
        db.Entry(user).Property(u => u.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds }));
    }

    private static async Task<Dictionary<Guid, List<Guid>>> RolesOf(IdentityDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.UserRoles.AsNoTracking().Where(ur => userIds.Contains(ur.UserId)).Select(ur => new { ur.UserId, ur.RoleId }).ToListAsync(cancellationToken))
        .GroupBy(x => x.UserId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).ToList());

    private static UserDto ToDto(User u, IReadOnlyDictionary<Guid, List<Guid>> roles) => new(
        u.Id, u.Email, u.DisplayName, u.Language, u.IsActive,
        roles.TryGetValue(u.Id, out var r) ? r : [],
        u.LastSignInAt, u.CreatedAt, u.Version);

    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}

internal static class RoleGrants
{
    /// <summary>True when every permission the roles grant is one the caller holds (no privilege
    /// escalation through role assignment).</summary>
    public static bool WithinCaller(IEnumerable<Role> roles, ICurrentUser caller) =>
        roles.SelectMany(r => r.Permissions).All(caller.Has);
}
