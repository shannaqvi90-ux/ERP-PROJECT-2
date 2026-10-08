using Erp.Kernel.Lists;
using Microsoft.AspNetCore.Http;

namespace Erp.Modules.Identity.Contracts;

public static class IdentityPermissions
{
    public const string UsersRead = "identity.users.read";
    public const string UsersCreate = "identity.users.create";
    public const string UsersUpdate = "identity.users.update";
    public const string UsersResetPassword = "identity.users.resetPassword";
    public const string UsersDelete = "identity.users.delete";
    public const string SignInsRead = "identity.signIns.read";
    public const string RolesRead = "identity.roles.read";
    public const string RolesCreate = "identity.roles.create";
    public const string RolesUpdate = "identity.roles.update";
    public const string RolesDelete = "identity.roles.delete";
    public const string ProfileUpdate = "identity.profile.update";

    public static readonly IReadOnlyList<string> All =
    [
        UsersRead, UsersCreate, UsersUpdate, UsersResetPassword, UsersDelete, SignInsRead, RolesRead, RolesCreate, RolesUpdate, RolesDelete, ProfileUpdate,
    ];
}

/// <summary>Keys of identity's registered lists other modules may name.</summary>
public static class IdentityLists
{
    /// <summary>The users list; its binding can serve another module's list of users.</summary>
    public const string Users = "identity.users";
}

/// <summary>Display facts about users other modules may show (for example "changed by").</summary>
public sealed record UserSummary(Guid Id, string DisplayName, string Email);

/// <summary>A page of users (newest first) and how many match in all.</summary>
public sealed record UserSummaryPage(IReadOnlyList<UserSummary> Items, int Total);

/// <summary>Reads users of the current tenant. Other modules use this instead of identity tables.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Users whose name or e-mail contains <paramref name="search"/> (all when empty), by name.</summary>
    Task<UserSummaryPage> SearchAsync(string? search, int skip, int take, CancellationToken cancellationToken);

    /// <summary>The permissions the user's roles grant (empty for a user without roles or one
    /// that does not exist). Another module that lets one user act on another (company access)
    /// compares them with the caller's: nobody acts on a user who holds more than they do.</summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The user with this e-mail (case-insensitive), or null.</summary>
    Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    /// <summary>One page of users for another module's list that identity's users serve
    /// (registered with <c>module.List(definition, servedBy: "identity.users")</c>): the list query
    /// contract (search, filter, sort, keyset or offset paging, grouping) over the current tenant's
    /// users, or the 400 problem naming the parameter to correct.</summary>
    Task<ListResult<UserSummary>> QueryListAsync(string listKey, ListRequest request, HttpContext http, CancellationToken cancellationToken);
}
