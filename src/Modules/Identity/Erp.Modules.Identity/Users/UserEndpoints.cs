using System.Text.Json.Serialization;
using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Identity.Auth;
using Erp.Kernel.Data;
using Erp.Modules.Identity.Contracts;
using Erp.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Modules.Identity.Users;

/// <summary>A user. <c>PendingSetup</c>: the account still has a one-time set-up code (invited or
/// reset, not yet signed in with a password of their own). The set-up code itself appears only in
/// the response that issued it. <c>RoleIds</c> apply in every company; <c>CompanyRoles</c> only in
/// their company (those of the companies the caller works in); <c>RolesElsewhere</c>: the user also
/// holds roles in companies the caller does not work in.</summary>
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
    IReadOnlyList<CompanyRoleDto>? CompanyRoles = null,
    bool RolesElsewhere = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SetupCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? SetupCodeExpiresAt = null);

/// <summary>New user. Without a password the user is invited: the response carries a one-time
/// set-up code to hand over, and the first sign-in chooses a password. With a password,
/// <c>MustChangePassword</c> makes it temporary. <c>DisplayNameAr</c>: the name in Arabic script,
/// shown on Arabic screens.</summary>
public sealed record CreateUserRequest(string? Email, string? DisplayName, [property: AllowedTextValues(Languages.English, Languages.Arabic)] string? Language, string? Password, IReadOnlyList<Guid>? RoleIds, bool? MustChangePassword = null, string? DisplayNameAr = null,
    IReadOnlyList<CompanyRoleRequest>? CompanyRoles = null);

/// <summary>A role held in one company only (one the caller works in).</summary>
public sealed record CompanyRoleRequest(Guid? RoleId, Guid? CompanyId);

/// <summary>A role the user holds in one company only.</summary>
public sealed record CompanyRoleDto(Guid RoleId, Guid CompanyId);

/// <summary>Edit a user. <c>Email</c> (optional) changes the sign-in address of another user, for
/// example to correct a typing mistake in an invitation; nobody changes their own sign-in here.
/// <c>CompanyRoles</c> (optional, left as they are when absent): every role the user holds in one
/// company, for the companies the caller works in; roles in other companies are left as they are.</summary>
public sealed record UpdateUserRequest(string? DisplayName, [property: AllowedTextValues(Languages.English, Languages.Arabic)] string? Language, bool? IsActive, IReadOnlyList<Guid>? RoleIds, uint? Version, string? Email = null, string? DisplayNameAr = null,
    IReadOnlyList<CompanyRoleRequest>? CompanyRoles = null);

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

/// <summary>A role the user holds: in every company (<c>CompanyId</c> null) or in one.</summary>
public sealed record AccessRole(Guid Id, string NameEn, string NameAr, bool IsSystem, Guid? CompanyId = null);

/// <summary>One reason a permission is held: a role, in every company (<c>CompanyId</c> null) or in one.</summary>
public sealed record AccessGrant(Guid RoleId, Guid? CompanyId);

/// <summary>A permission the user holds and the roles that grant it (<c>GrantedBy</c>), with where
/// each one applies (<c>Grants</c>).</summary>
public sealed record AccessPermission(string Key, string Module, string Label, string ModuleLabel, IReadOnlyList<Guid> GrantedBy, IReadOnlyList<AccessGrant>? Grants = null);

/// <summary>A company named in a user's access (one the caller works in).</summary>
public sealed record AccessCompany(Guid Id, string Code, string LegalNameEn, string LegalNameAr);

/// <summary>What a user can do, and why: their roles (in every company or in one) and each
/// permission with the roles that grant it and where. <c>RolesElsewhere</c>: the user also holds
/// roles in companies the caller does not work in, which are not shown.</summary>
public sealed record UserAccessDto(Guid UserId, IReadOnlyList<AccessRole> Roles, IReadOnlyList<AccessPermission> Permissions,
    IReadOnlyList<AccessCompany>? Companies = null, bool RolesElsewhere = false);

/// <summary>The company a user works in by default (null: none chosen, their first company by code
/// is used), the companies (of the caller's) they may work in, and the version to send back.</summary>
public sealed record DefaultCompanyDto(Guid UserId, Guid? CompanyId, IReadOnlyList<AccessCompany> Companies, uint Version);

/// <summary>Set the company a user works in by default (null: none, their first company by code).
/// <c>Version</c>: the one read.</summary>
public sealed record DefaultCompanyRequest(Guid? CompanyId, uint? Version);

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

        group.MapGet("/users/{id:guid}/default-company", GetDefaultCompany)
            .WithName("identity.users.defaultCompany")
            .WithSummary("The company the user works in by default (until they switch in the top bar), and the companies of the caller's they may work in.")
            .RequirePermission(IdentityPermissions.UsersRead);

        group.MapPut("/users/{id:guid}/default-company", SetDefaultCompany)
            .WithName("identity.users.setDefaultCompany")
            .WithSummary("Set the company another user works in by default (null: their first company by code). Only a company of the caller's the user may work in, only for users whose access is within the caller's own and who work in no company the caller does not; never on oneself (switch in the top bar instead).")
            .ProducesValidationProblem()
            .RequirePermission(IdentityPermissions.UsersUpdate);

        group.MapGet("/companies", Companies)
            .WithName("identity.companies.list")
            .WithSummary("The companies the caller works in: where the caller may assign roles that apply in one company, and name a default company.")
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
        var companyRoles = await CompanyRolesOf(db, ids, cancellationToken);
        return TypedResults.Ok(result.ToPage(u => ToDto(u, roles, pending, companyRoles)));
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> Get(Guid id, IdentityDbContext db, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        return TypedResults.Ok(ToDto(user, await RolesOf(db, [user.Id], cancellationToken), await PendingSetupOf(db, [user.Id], cancellationToken),
            await CompanyRolesOf(db, [user.Id], cancellationToken)));
    }

    private static async Task<Results<Created<UserDto>, ProblemHttpResult>> Create(
        CreateUserRequest request, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, ICompanyContext scope, IOptions<AuthOptions> options, TimeProvider time,
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
        var companyRoles = await CompanyRoles.ReadAsync(request.CompanyRoles ?? [], db, scope, validator, cancellationToken);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        var callerGrants = await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken);
        if (!RoleGrants.WithinCaller(roles, callerGrants) || !CompanyRoles.WithinCaller(companyRoles, callerGrants))
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
        db.UserCompanyRoles.AddRange(companyRoles.Select(c => new UserCompanyRole { UserId = user.Id, RoleId = c.Role.Id, CompanyId = c.CompanyId }));
        await db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds }, mustChange ? new HashSet<Guid> { user.Id } : new HashSet<Guid>(),
                new Dictionary<Guid, List<CompanyRoleDto>> { [user.Id] = companyRoles.Select(c => new CompanyRoleDto(c.Role.Id, c.CompanyId)).ToList() })
            with { SetupCode = setupCode, SetupCodeExpiresAt = expiresAt, RolesElsewhere = false };
        return TypedResults.Created($"/api/identity/users/{user.Id}", dto);
    }

    private static async Task<Results<Ok<UserDto>, ProblemHttpResult>> Update(
        Guid id, UpdateUserRequest request, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, ICompanyContext scope, HttpContext http, CancellationToken cancellationToken)
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
        var wantedCompanyRoles = request.CompanyRoles is { } asked ? await CompanyRoles.ReadAsync(asked, db, scope, validator, cancellationToken) : null;
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
        // Company roles of the caller's companies (row-level security shows no others).
        var currentCompanyRoles = await db.UserCompanyRoles.Where(r => r.UserId == id).ToListAsync(cancellationToken);
        var companyRolesChanged = wantedCompanyRoles is not null &&
            !currentCompanyRoles.Select(c => (c.RoleId, c.CompanyId)).ToHashSet().SetEquals(wantedCompanyRoles.Select(w => (w.Role.Id, w.CompanyId)));
        var normalized = email?.ToLowerInvariant();
        var emailChanged = email is not null && email != user.Email;
        if (user.Id == caller.UserId && (rolesChanged || companyRolesChanged || request.IsActive == false || emailChanged))
        {
            return Problems.Forbidden(http, "identity.cannotChangeOwnAccess");
        }
        var callerGrants = await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken);
        if (user.Id != caller.UserId && await UserAccess.RefusalAsync(db, catalog, id, callerGrants, cancellationToken) is { } refusal)
        {
            return Problems.Forbidden(http, refusal);
        }
        if (rolesChanged)
        {
            // Every role being added or removed must stay within the caller's own permissions.
            var removedIds = current.Select(c => c.RoleId).Except(roleIds).ToList();
            var removed = await db.Roles.AsNoTracking().Where(r => removedIds.Contains(r.Id)).ToListAsync(cancellationToken);
            var added = roles.Where(r => current.All(c => c.RoleId != r.Id)).ToList();
            if (!RoleGrants.WithinCaller(added.Concat(removed), callerGrants))
            {
                return Problems.Forbidden(http, "identity.grantBeyondOwn");
            }
            db.UserRoles.RemoveRange(current.Where(c => !roleIds.Contains(c.RoleId)));
            db.UserRoles.AddRange(roleIds.Where(r => current.All(c => c.RoleId != r)).Select(r => new UserRole { UserId = id, RoleId = r }));
        }
        Action? companyRoleChanges = null;
        if (companyRolesChanged)
        {
            // Every company role being added or removed must stay within what the caller holds in that company.
            var wanted = wantedCompanyRoles!;
            var removedRows = currentCompanyRoles.Where(c => !wanted.Any(w => w.Role.Id == c.RoleId && w.CompanyId == c.CompanyId)).ToList();
            var removedRoleIds = removedRows.Select(r => r.RoleId).Distinct().ToList();
            var removedRoles = await db.Roles.AsNoTracking().Where(r => removedRoleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
            var addedRoles = wanted.Where(w => !currentCompanyRoles.Any(c => c.RoleId == w.Role.Id && c.CompanyId == w.CompanyId)).ToList();
            var changed = addedRoles.Concat(removedRows.Select(r => new CompanyRoles.Item(removedRoles[r.RoleId], r.CompanyId)));
            if (!CompanyRoles.WithinCaller(changed, callerGrants))
            {
                return Problems.Forbidden(http, "identity.grantBeyondOwn");
            }
            companyRoleChanges = () =>
            {
                db.UserCompanyRoles.RemoveRange(removedRows);
                db.UserCompanyRoles.AddRange(addedRoles.Select(a => new UserCompanyRole { UserId = id, RoleId = a.Role.Id, CompanyId = a.CompanyId }));
            };
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
        if (companyRoleChanges is not null)
        {
            // After the version check of the user row: the trigger counting company roles writes
            // that row too.
            companyRoleChanges();
            await db.SaveChangesAsync(cancellationToken);
        }
        // Read back: the trigger keeps the company role count (and the version) of the user row.
        await db.Entry(user).ReloadAsync(cancellationToken);
        return TypedResults.Ok(ToDto(user, new Dictionary<Guid, List<Guid>> { [user.Id] = roleIds }, await PendingSetupOf(db, [user.Id], cancellationToken),
            await CompanyRolesOf(db, [user.Id], cancellationToken)));
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
        // A concurrent request may have removed the user since the target check.
        if (await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) is not { } user)
        {
            return Problems.NotFound(http);
        }
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
        // A concurrent request may have removed the user since the target check.
        if (await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken) is not { } user)
        {
            return Problems.NotFound(http);
        }
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
        Guid id, IdentityDbContext db, ModuleCatalog catalog, StringCatalog strings, ICompanyDirectory companies, HttpContext http, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new { u.CompanyRoleCount }).SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return Problems.NotFound(http);
        }
        var language = Languages.ForRequest(http);
        var everywhere = await (
                from userRole in db.UserRoles
                where userRole.UserId == id
                join role in db.Roles on userRole.RoleId equals role.Id
                select role)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var inCompanies = await (
                from userRole in db.UserCompanyRoles
                where userRole.UserId == id
                join role in db.Roles on userRole.RoleId equals role.Id
                select new { Role = role, userRole.CompanyId })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var held = everywhere.Select(r => (Role: r, CompanyId: (Guid?)null))
            .Concat(inCompanies.Select(c => (c.Role, CompanyId: (Guid?)c.CompanyId)))
            .OrderBy(h => h.CompanyId is null ? 0 : 1).ThenByDescending(h => h.Role.IsSystem).ThenBy(h => h.Role.NameEn, StringComparer.Ordinal).ThenBy(h => h.CompanyId)
            .ToList();
        var permissions = catalog.Permissions
            .Select(p => (Definition: p, Grants: held.Where(h => h.Role.Permissions.Contains(p.Key)).ToList()))
            .Where(p => p.Grants.Count > 0)
            .Select(p => new AccessPermission(p.Definition.Key, p.Definition.Module, strings.Get(p.Definition.LabelKey, language),
                strings.Get($"module.{p.Definition.Module}", language), p.Grants.Select(g => g.Role.Id).Distinct().ToList(),
                p.Grants.Select(g => new AccessGrant(g.Role.Id, g.CompanyId)).ToList()))
            .OrderBy(p => p.Module, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
        var named = inCompanies.Select(c => c.CompanyId).ToHashSet();
        var companyList = (await companies.ListAsync(cancellationToken)).Where(c => named.Contains(c.Id))
            .Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList();
        return TypedResults.Ok(new UserAccessDto(id,
            held.Select(h => new AccessRole(h.Role.Id, h.Role.NameEn, h.Role.NameAr, h.Role.IsSystem, h.CompanyId)).ToList(),
            permissions, companyList, user.CompanyRoleCount > inCompanies.Count));
    }

    private static async Task<Results<Ok<DefaultCompanyDto>, ProblemHttpResult>> GetDefaultCompany(
        Guid id, IdentityDbContext db, IUserWorkplaces workplaces, HttpContext http, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            return Problems.NotFound(http);
        }
        var info = await workplaces.GetAsync(id, cancellationToken);
        return TypedResults.Ok(new DefaultCompanyDto(id, info.CompanyId,
            info.Companies.Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList(), info.Version));
    }

    private static async Task<Results<Ok<DefaultCompanyDto>, ProblemHttpResult>> SetDefaultCompany(
        Guid id, DefaultCompanyRequest request, IdentityDbContext db, ICurrentUser caller, ModuleCatalog catalog, IUserWorkplaces workplaces,
        HttpContext http, CancellationToken cancellationToken)
    {
        var validator = new Validator(http).Required("version", request.Version);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        // Who it is for comes first: a refusal never depends on the company asked for.
        if (await TargetProblemAsync(db, catalog, id, caller, http, cancellationToken) is { } problem)
        {
            return problem;
        }
        switch (await workplaces.SetAsync(id, request.CompanyId, request.Version!.Value, cancellationToken))
        {
            case WorkplaceChange.CompanyNotAllowed:
                return validator.Add("companyId", "identityNotTheirCompany").ToResult();
            case WorkplaceChange.UserBeyondScope:
                return Problems.Forbidden(http, "identity.userBeyondOwnCompanies");
            case WorkplaceChange.Stale:
                return Problems.Conflict(http, "concurrency");
        }
        var info = await workplaces.GetAsync(id, cancellationToken);
        return TypedResults.Ok(new DefaultCompanyDto(id, info.CompanyId,
            info.Companies.Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList(), info.Version));
    }

    private static async Task<Ok<List<AccessCompany>>> Companies(ICompanyDirectory companies, CancellationToken cancellationToken) =>
        TypedResults.Ok((await companies.ListAsync(cancellationToken)).Where(c => c.IsActive)
            .Select(c => new AccessCompany(c.Id, c.Code, c.LegalNameEn, c.LegalNameAr)).ToList());

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
        var callerGrants = await GrantQueries.ForCallerAsync(db, caller, catalog, cancellationToken);
        if (await UserAccess.RefusalAsync(db, catalog, id, callerGrants, cancellationToken) is { } refusal)
        {
            return Problems.Forbidden(http, refusal);
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

    /// <summary>The company roles of the caller's companies (row-level security shows no others), by user.</summary>
    private static async Task<Dictionary<Guid, List<CompanyRoleDto>>> CompanyRolesOf(IdentityDbContext db, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.UserCompanyRoles.AsNoTracking().Where(r => userIds.Contains(r.UserId)).Select(r => new { r.UserId, r.RoleId, r.CompanyId }).ToListAsync(cancellationToken))
        .GroupBy(x => x.UserId)
        .ToDictionary(g => g.Key, g => g.Select(x => new CompanyRoleDto(x.RoleId, x.CompanyId)).OrderBy(x => x.CompanyId).ThenBy(x => x.RoleId).ToList());

    private static UserDto ToDto(User u, IReadOnlyDictionary<Guid, List<Guid>> roles, IReadOnlySet<Guid> pending, IReadOnlyDictionary<Guid, List<CompanyRoleDto>> companyRoles)
    {
        var inCompanies = companyRoles.TryGetValue(u.Id, out var c) ? c : [];
        return new UserDto(
            u.Id, u.Email, u.DisplayName, u.Language, u.IsActive,
            roles.TryGetValue(u.Id, out var r) ? r : [],
            u.LastSignInAt, u.CreatedAt, u.Version, pending.Contains(u.Id), u.DisplayNameAr,
            CompanyRoles: inCompanies, RolesElsewhere: u.CompanyRoleCount > inCompanies.Count);
    }

    /// <summary>Trimmed text, or null when blank.</summary>
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class RoleGrants
{
    /// <summary>True when the caller holds, in every company, every permission the roles grant: a
    /// role assigned in the whole workspace grants it everywhere (no privilege escalation through
    /// role assignment).</summary>
    public static bool WithinCaller(IEnumerable<Role> roles, UserGrants caller) =>
        roles.SelectMany(r => r.Permissions).All(p => caller.Covers(p, null));
}

/// <summary>Roles assigned in one company: read from a request, and checked against the caller.</summary>
internal static class CompanyRoles
{
    public const int MaxPerUser = 200;

    public sealed record Item(Role Role, Guid CompanyId);

    /// <summary>The request's company roles (duplicates once), with a field error for a missing or
    /// unknown role, or a company outside the caller's scope (told apart from no company at all:
    /// both are unknown ids).</summary>
    public static async Task<List<Item>> ReadAsync(IReadOnlyList<CompanyRoleRequest> asked, IdentityDbContext db, ICompanyContext scope, Validator validator,
        CancellationToken cancellationToken)
    {
        validator.Must(asked.Count <= MaxPerUser, "companyRoles", "identityTooManyCompanyRoles", MaxPerUser);
        if (asked.Any(a => a is null || a.RoleId is null || a.CompanyId is null))
        {
            validator.Add("companyRoles", "required");
            return [];
        }
        var pairs = asked.Select(a => (RoleId: a.RoleId!.Value, CompanyId: a.CompanyId!.Value)).Distinct().ToList();
        var roleIds = pairs.Select(p => p.RoleId).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var known = pairs.All(p => roles.ContainsKey(p.RoleId) && scope.AllowsCompany(p.CompanyId));
        validator.Must(known, "companyRoles", "unknownIds");
        return known ? pairs.Select(p => new Item(roles[p.RoleId], p.CompanyId)).ToList() : [];
    }

    /// <summary>True when the caller holds what each role grants in its company (or everywhere).</summary>
    public static bool WithinCaller(IEnumerable<Item> items, UserGrants caller) =>
        items.All(i => caller.CoversAll(i.Role.Permissions, i.CompanyId));
}

internal static class UserAccess
{
    /// <summary>
    /// Why the caller may not act on this user, or null. Acting on a stronger account (resetting its
    /// password, ending its sessions, editing it) would be a way to take it over: the caller must
    /// hold every permission the user holds, in every company where the user holds it (or
    /// everywhere), and the user may hold no role in a company the caller does not work in (what it
    /// grants cannot be seen from here).
    /// </summary>
    public static async Task<string?> RefusalAsync(IdentityDbContext db, ModuleCatalog catalog, Guid userId, UserGrants caller, CancellationToken cancellationToken)
    {
        var held = await GrantQueries.ForUserAsync(db, userId, catalog, ownRows: false, cancellationToken);
        if (held.Hidden > 0)
        {
            return "identity.userBeyondOwnCompanies";
        }
        return caller.CoversAll(held) ? null : "identity.userBeyondOwn";
    }
}
