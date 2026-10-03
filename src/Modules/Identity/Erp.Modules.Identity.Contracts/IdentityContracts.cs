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

/// <summary>Display facts about users other modules may show (for example "changed by").</summary>
public sealed record UserSummary(Guid Id, string DisplayName, string Email);

/// <summary>Reads users of the current tenant. Other modules use this instead of identity tables.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}
