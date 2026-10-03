using System.Text.Json.Serialization;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Auth;
using Erp.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Users;

/// <summary>A user. <c>PendingSetup</c>: the account still has a one-time set-up code (invited or
/// reset, not yet signed in with a password of their own). The set-up code itself appears only in
/// the response that issued it.</summary>
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Language,
    bool IsActive,
    IReadOnlyList<Guid> RoleIds,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset CreatedAt,
    uint Version,
    bool PendingSetup = false,
    string? DisplayNameAr = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SetupCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SetupCodeExpiresAt = null);

/// <summary>New user. Without a password the user is invited: the response carries a one-time
/// set-up code to hand over, and the first sign-in chooses a password. With a password,
/// <c>MustChangePassword</c> makes it temporary. <c>DisplayNameAr</c>: the name in Arabic script,
/// shown on Arabic screens.</summary>
public sealed record CreateUserRequest(string? Email, string? DisplayName, string? Language, string? Password, IReadOnlyList<Guid>? RoleIds, bool? MustChangePassword = null, string? DisplayNameAr = null);

/// <summary>Edit a user. <c>Email</c> (optional) changes the sign-in address of another user, for
/// example to correct a typing mistake in an invitation; nobody changes their own sign-in here.</summary>
public sealed record UpdateUserRequest(string? DisplayName, string? Language, bool? IsActive, IReadOnlyList<Guid>? RoleIds, uint? Version, string? Email = null, string? DisplayNameAr = null);

/// <summary>Reset a user's password. Without a password: a new one-time set-up code. With one:
/// that password, temporary unless <c>MustChangePassword</c> is false.</summary>
public sealed record ResetPasswordRequest(string? Password, bool? MustChangePassword);

public sealed record ResetPasswordResponse(
    bool MustChangePassword,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SetupCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SetupCodeExpiresAt,
    int SessionsEnded);

public sealed record SessionsEndedResponse(int SessionsEnded);

/// <summary>One attempt in a user's sign-in history.</summary>
public sealed record SignInDto(Guid Id, DateTimeOffset OccurredAt, string Outcome, string? IpAddress, string? UserAgent, bool SessionActive);

/// <summary>A client that may not sign in to this account until <c>Until</c> (too many failures).</summary>
public sealed record PausedClient(string Source, DateTimeOffset Until, int Failures);

public sealed record SignInHistoryPage(IReadOnlyList<SignInDto> Items, int Total, IReadOnlyList<PausedClient> Paused);

public sealed record AccessRole(Guid Id, string NameEn, string NameAr, bool IsSystem);

/// <summary>A permission the user holds and the roles that grant it.</summary>
public sealed record AccessPermission(string Key, string Module, string Label, string ModuleLabel, IReadOnlyList<Guid> GrantedBy);

/// <summary>What a user can do, and why: their roles and each permission with the roles that grant it.</summary>
public sealed record UserAccessDto(Guid UserId, IReadOnlyList<AccessRole> Roles, IReadOnlyList<AccessPermission> Permissions);

internal static class UserEndpoints
{
    public const int MaxPageSize = 200;

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/users", List)
            .WithName("identity.users.list")
            .WithSummary("Users of the workspace, a page at a time: word search on name and e-mail, filters, sort, keyset or offset paging and grouping (the list query contract); newest first by default.")
            .RequirePermission(IdentityPermissions.UsersRead);

        group.MapGet("/users/{id:guid}", Get)
            .WithName("identity.users.get")
            .WithSummary("One user.")
            .RequirePermission(IdentityPermissions.UsersRead);

        group.MapPost("/users", Create)
            .WithName("identity.users.create")
            .WithSummary("Create a user with roles. Without a password the user is invited and the answer carries a one-time set-up code. Roles may only grant permissions the caller holds.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersCreate);

        group.MapPut("/users/{id:guid}", Update)
            .WithName("identity.users.update")
            .WithSummary("Change a user's name, language, active flag and roles. Only users whose access is within the caller's own; callers cannot change their own roles or deactivate themselves.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersUpdate);

        group.MapPost("/users/{id:guid}/password", ResetPassword)
            .WithName("identity.users.resetPassword")
            .WithSummary("Reset another user's password: a new one-time set-up code, or a given (temporary) password. Ends the user's sessions. Only users whose access is within the caller's own.")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersResetPassword);

        group.MapPost("/users/{id:guid}/unblock", Unblock)
            .WithName("identity.users.unblock")
            .WithSummary("Clear every sign-in pause on another user's account (failed attempts before now stop counting).")
            .RequirePermission(IdentityPermissions.UsersUpdate);

        group.MapDelete("/users/{id:guid}", Delete)
            .WithName("identity.users.delete")
            .WithSummary("Delete a user who has never signed in (for example an invitation sent to a mistyped address). Anyone who has signed in keeps their record for the audit trail: deactivate them instead. Only a caller who holds every permission the user holds, never on themselves.")
            .RequirePermission(IdentityPermissions.UsersDelete);

        group.MapPost("/users/{id:guid}/sessions/revoke", RevokeSessions)
            .WithName("identity.users.revokeSessions")
            .WithSummary("Sign another user out everywhere: ends all of their sessions.")
            .RequirePermission(IdentityPermissions.UsersUpdate);

        group.MapGet("/users/{id:guid}/access", Access)
            .WithName("identity.users.access")
            .WithSummary("What the user can do and why: their roles, and every permission they hold with the roles that grant it.")
            .RequirePermission(IdentityPermissions.UsersRead);

        group.MapGet("/users/{id:guid}/sign-ins", SignIns)
            .WithName("identity.users.signIns")
            .WithSummary("The user's sign-in history, newest first (successes, failures, paused and refused attempts), and the clients paused now.")
            .RequirePermission(IdentityPermissions.SignInsRead);
    }

    private static async Task<Results<Ok<ListPage<UserDto>>, ProblemHttpResult>> List(
        IdentityDbContext db, ModuleCatalog catalog, [AsParameters] ListRequest request, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await catalog.ListBinding<User>(UsersList.Key).QueryAsync(db.Users.AsNoTracking(), request, http, cancellationToken);
        if (result.Problem is { } problem)
        {
            return problem;
        }
        var ids = result.Rows.Select(u => u.Id).ToList();
        var roles = await RolesOf(db, ids, cancellationToken);
        var pending = await PendingSetupOf(db, ids, cancellationToken);
        return TypedResults.Ok(result.ToPage(u => ToDto(u, roles, pending)));
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> Get(Guid id, IdentityDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(ToDto(user, await RolesOf(db, [user.Id], cancellationToken), await PendingSetupOf(db, [user.Id], cancellationToken)));
    }

    private static async Task<Results<Created<UserDto>, ProblemHttpResult>> Create(
        CreateUserRequest request, IdentityDbContext db, ICurrentUser caller, IOptions<AuthOptions> options, TimeProvider time,
        HttpContext http, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();
        var validator = new Validator(http)
            .Required("email", email).Email("email", email)
            .Required("displayName", request.DisplayName).MaxLength("displayName", request.DisplayName, 200)
            .MaxLength("displayNameAr", request.DisplayNameAr, 200)
            .Required("language", request.Language).OneOf("language", request.Language, Languages.All);
        if (!string.IsNullOrEmpty(request.Password) && Passwords.Problem(request.Password) is { } refusal)
        {
            validator.Add("password", refusal.Code, refusal.Args);
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
        var now = time.GetUtcNow();
        var user = new User
        {
            Email = email,
            EmailNormalized = normalized,
            DisplayName = request.DisplayName!.Trim(),
            DisplayNameAr = Optional(request.DisplayNameAr),
            Language = request.Language!,
        };
        db.Users.Add(user);
        string? setupCode = null;
        DateTimeOffset? expiresAt = null;
        var mustChange = true;
        if (string.IsNullOrEmpty(request.Password))
        {
            setupCode = Passwords.NewSetupCode();
            expiresAt = now.AddHours(options.Value.SetupCodeHours);
            Passwords.Add(db, user.Id, setupCode, mustChange: true, expiresAt, now, caller.UserId);
        }
        else
        {
            mustChange = request.MustChangePassword == true;
            Passwords.Add(db, user.Id, request.Password, mustChange, expiresAt: null, now, caller.UserId);
        }
        db.UserRoles.AddRange(roleIds.Select(r => new UserRole { UserId = user.Id, RoleId = r }));
        await db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds }, mustChange ? new HashSet<Guid> { user.Id } : new HashSet<Guid>())
            with { SetupCode = setupCode, SetupCodeExpiresAt = expiresAt };
        return TypedResults.Created($"/api/identity/users/{user.Id}", dto);
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> Update(
        Guid id, UpdateUserRequest request, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, HttpContext http, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim();
        var validator = new Validator(http)
            .Required("displayName", request.DisplayName).MaxLength("displayName", request.DisplayName, 200)
            .MaxLength("displayNameAr", request.DisplayNameAr, 200)
            .Required("language", request.Language).OneOf("language", request.Language, Languages.All)
            .Required("isActive", request.IsActive)
            .Required("version", request.Version);
        if (email is not null)
        {
            validator.Required("email", email).Email("email", email);
        }
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
        var normalized = email?.ToLowerInvariant();
        var emailChanged = email is not null && email != user.Email;
        if (user.Id == caller.UserId && (rolesChanged || request.IsActive == false || emailChanged))
        {
            return Problems.Forbidden(http, "identity.cannotChangeOwnAccess");
        }
        if (user.Id != caller.UserId && !await UserAccess.WithinCallerAsync(db, catalog, id, caller, cancellationToken))
        {
            return Problems.Forbidden(http, "identity.userBeyondOwn");
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
        if (emailChanged && normalized != user.EmailNormalized && await db.Users.AnyAsync(u => u.EmailNormalized == normalized && u.Id != id, cancellationToken))
        {
            return Problems.Conflict(http, "identity.emailTaken");
        }
        db.Entry(user).Property(u => u.Version).OriginalValue = request.Version!.Value;
        if (emailChanged)
        {
            user.Email = email!;
            user.EmailNormalized = normalized!;
        }
        user.DisplayName = request.DisplayName!.Trim();
        if (request.DisplayNameAr is not null)
        {
            user.DisplayNameAr = Optional(request.DisplayNameAr);
        }
        user.Language = request.Language!;
        user.IsActive = request.IsActive!.Value;
        // Force a version check even when only roles changed.
        db.Entry(user).Property(u => u.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds }, await PendingSetupOf(db, [user.Id], cancellationToken)));
    }

    private static async Task<Results<Ok<ResetPasswordResponse>, ProblemHttpResult>> ResetPassword(
        Guid id, ResetPasswordRequest request, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, IOptions<AuthOptions> options,
        TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http);
        if (!string.IsNullOrEmpty(request.Password) && Passwords.Problem(request.Password) is { } refusal)
        {
            validator.Add("password", refusal.Code, refusal.Args);
        }
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        var now = time.GetUtcNow();
        string? setupCode = null;
        DateTimeOffset? expiresAt = null;
        bool mustChange;
        if (string.IsNullOrEmpty(request.Password))
        {
            setupCode = Passwords.NewSetupCode();
            expiresAt = now.AddHours(options.Value.SetupCodeHours);
            mustChange = true;
            await Passwords.SetAsync(db, id, setupCode, mustChange, expiresAt, now, caller.UserId, cancellationToken);
        }
        else
        {
            mustChange = request.MustChangePassword != false;
            await Passwords.SetAsync(db, id, request.Password, mustChange, expiresAt: null, now, caller.UserId, cancellationToken);
        }
        var ended = await EndSessionsAsync(db, id, now, cancellationToken);
        return TypedResults.Ok(new ResetPasswordResponse(mustChange, setupCode, expiresAt, ended));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Unblock(
        Guid id, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        var user = await db.Users.SingleAsync(u => u.Id == id, cancellationToken);
        user.SignInUnblockedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        Guid id, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, HttpContext http, CancellationToken cancellationToken)
    {
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        var user = await db.Users.SingleAsync(u => u.Id == id, cancellationToken);
        // Sign-in history is append-only (the application role may not delete it), so an account
        // anyone has tried to sign in to stays, and so does everyone who ever signed in.
        if (user.LastSignInAt is not null || await db.Sessions.AnyAsync(s => s.UserId == id, cancellationToken) ||
            await db.SignInAttempts.AnyAsync(a => a.UserId == id, cancellationToken))
        {
            return Problems.Conflict(http, "identity.userHasSignedIn");
        }
        // Removed one by one so the row trigger audits each removal; the audit trail keeps the
        // user's history (who created them, when, with which roles) after the record is gone.
        db.UserRoles.RemoveRange(await db.UserRoles.Where(ur => ur.UserId == id).ToListAsync(cancellationToken));
        await db.Credentials.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SessionsEndedResponse>, ProblemHttpResult>> RevokeSessions(
        Guid id, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        return TypedResults.Ok(new SessionsEndedResponse(await EndSessionsAsync(db, id, time.GetUtcNow(), cancellationToken)));
    }

    private static async Task<Results<Ok<UserAccessDto>, ProblemHttpResult>> Access(
        Guid id, IdentityDbContext db, ModuleCatalog catalog, StringCatalog strings, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        var language = Languages.ForRequest(http);
        var roles = await (
                from userRole in db.UserRoles
                where userRole.UserId == id
                join role in db.Roles on userRole.RoleId equals role.Id
                select role)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        roles = roles.OrderByDescending(r => r.IsSystem).ThenBy(r => r.NameEn, StringComparer.Ordinal).ToList();
        var permissions = catalog.Permissions
            .Select(p => (Definition: p, GrantedBy: roles.Where(r => r.Permissions.Contains(p.Key)).Select(r => r.Id).ToList()))
            .Where(p => p.GrantedBy.Count > 0)
            .Select(p => new AccessPermission(p.Definition.Key, p.Definition.Module, strings.Get(p.Definition.LabelKey, language),
                strings.Get($"module.{p.Definition.Module}", language), p.GrantedBy))
            .OrderBy(p => p.Module, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
        return TypedResults.Ok(new UserAccessDto(id, roles.Select(r => new AccessRole(r.Id, r.NameEn, r.NameAr, r.IsSystem)).ToList(), permissions));
    }

    private static async Task<Results<Ok<SignInHistoryPage>, ProblemHttpResult>> SignIns(
        Guid id, int? skip, int? take, IdentityDbContext db, IOptions<AuthOptions> options, TimeProvider time, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.SignInUnblockedAt }).SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        var query = db.SignInAttempts.AsNoTracking().Where(a => a.UserId == id);
        var total = await query.CountAsync(cancellationToken);
        var now = time.GetUtcNow();
        var rows = await query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .Select(a => new
            {
                a.Id,
                a.OccurredAt,
                a.Outcome,
                a.IpAddress,
                a.UserAgent,
                Active = a.SessionId != null && db.Sessions.Any(s => s.Id == a.SessionId && s.RevokedAt == null && s.ExpiresAt > now),
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(a => new SignInDto(a.Id, a.OccurredAt, a.Outcome, a.IpAddress, a.UserAgent, a.Active)).ToList();
        return TypedResults.Ok(new SignInHistoryPage(items, total, await PausedAsync(db, id, user.SignInUnblockedAt, options.Value, now, cancellationToken)));
    }

    /// <summary>The clients paused on this account now, by the rule the sign-in function applies:
    /// at least the threshold of failures within the window, counted since the client's last
    /// success and the last unblock.</summary>
    internal static async Task<IReadOnlyList<PausedClient>> PausedAsync(IdentityDbContext db, Guid userId, DateTimeOffset? unblockedAt,
        AuthOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var threshold = Math.Clamp(options.LockoutThreshold, 3, 50);
        var window = TimeSpan.FromMinutes(Math.Clamp(options.LockoutMinutes, 1, 1440));
        var from = now - window;
        var recent = await db.SignInAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.OccurredAt > from && (a.Outcome == SignInOutcomes.Failed || a.Outcome == SignInOutcomes.Succeeded))
            .Select(a => new { a.Source, a.Outcome, a.OccurredAt })
            .ToListAsync(cancellationToken);
        var paused = new List<PausedClient>();
        foreach (var group in recent.GroupBy(a => a.Source))
        {
            var lastSuccess = group.Where(a => a.Outcome == SignInOutcomes.Succeeded).Select(a => (DateTimeOffset?)a.OccurredAt).Max();
            var since = new[] { from, unblockedAt ?? DateTimeOffset.MinValue, lastSuccess ?? DateTimeOffset.MinValue }.Max();
            var failures = group.Where(a => a.Outcome == SignInOutcomes.Failed && a.OccurredAt > since)
                .Select(a => a.OccurredAt).OrderByDescending(t => t).ToList();
            if (failures.Count >= threshold)
            {
                // Paused until enough of the failures leave the window.
                paused.Add(new PausedClient(group.Key, failures[threshold - 1] + window, failures.Count));
            }
        }
        return paused.OrderBy(p => p.Source, StringComparer.Ordinal).ToList();
    }

    /// <summary>The problem for acting on <paramref name="id"/>: it does not exist, it is the
    /// caller (administration endpoints act on others), or its access exceeds the caller's.</summary>
    private static async Task<ProblemHttpResult?> TargetProblemAsync(IdentityDbContext db, ModuleCatalog catalog, Guid id, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        if (id == caller.UserId)
        {
            return Problems.Forbidden(http, "identity.notOnYourself");
        }
        if (!await UserAccess.WithinCallerAsync(db, catalog, id, caller, cancellationToken))
        {
            return Problems.Forbidden(http, "identity.userBeyondOwn");
        }
        return null;
    }

    private static Task<int> EndSessionsAsync(IdentityDbContext db, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

    private static async Task<Dictionary<Guid, List<Guid>>> RolesOf(IdentityDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.UserRoles.AsNoTracking().Where(ur => userIds.Contains(ur.UserId)).Select(ur => new { ur.UserId, ur.RoleId }).ToListAsync(cancellationToken))
        .GroupBy(x => x.UserId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.RoleId).ToList());

    private static async Task<HashSet<Guid>> PendingSetupOf(IdentityDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.Credentials.Where(c => userIds.Contains(c.Id) && c.MustChange).Select(c => c.Id).ToListAsync(cancellationToken)).ToHashSet();

    private static UserDto ToDto(User u, IReadOnlyDictionary<Guid, List<Guid>> roles, IReadOnlySet<Guid> pending) => new(
        u.Id, u.Email, u.DisplayName, u.Language, u.IsActive,
        roles.TryGetValue(u.Id, out var r) ? r : [],
        u.LastSignInAt, u.CreatedAt, u.Version, pending.Contains(u.Id), u.DisplayNameAr);

    /// <summary>Trimmed text, or null when blank.</summary>
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class RoleGrants
{
    /// <summary>True when every permission the roles grant is one the caller holds (no privilege
    /// escalation through role assignment).</summary>
    public static bool WithinCaller(IEnumerable<Role> roles, ICurrentUser caller) =>
        roles.SelectMany(r => r.Permissions).All(caller.Has);
}

internal static class UserAccess
{
    /// <summary>True when every permission the user's roles grant is one the caller holds: acting
    /// on a stronger account (resetting its password, ending its sessions, editing it) would be a
    /// way to take it over.</summary>
    public static async Task<bool> WithinCallerAsync(IdentityDbContext db, ModuleCatalog catalog, Guid userId, ICurrentUser caller, CancellationToken cancellationToken)
    {
        var held = await PermissionQueries.ForUserAsync(db, userId, catalog, cancellationToken);
        return held.All(caller.Has);
    }
}
