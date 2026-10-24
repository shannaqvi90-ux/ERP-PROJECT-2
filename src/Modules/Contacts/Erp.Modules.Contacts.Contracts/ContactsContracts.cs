namespace Erp.Modules.Contacts.Contracts;

/// <summary>The permissions of the contacts directory. Reading, creating and changing are separate,
/// and so is archiving and restoring: a clerk who keeps addresses current need not be able to take
/// a customer out of use.</summary>
public static class ContactsPermissions
{
    public const string ContactsRead = "contacts.contacts.read";
    public const string ContactsCreate = "contacts.contacts.create";
    public const string ContactsUpdate = "contacts.contacts.update";
    public const string ContactsArchive = "contacts.contacts.archive";

    public static readonly IReadOnlyList<string> All = [ContactsRead, ContactsCreate, ContactsUpdate, ContactsArchive];
}

/// <summary>Keys of the lists the contacts module registers.</summary>
public static class ContactsLists
{
    /// <summary>The shared contacts directory (organisations and people).</summary>
    public const string Contacts = "contacts.contacts";
}

/// <summary>Whether a contact is an organisation (a customer company, a supplier, a government
/// body) or a person (who may work for an organisation).</summary>
public static class ContactKinds
{
    public const string Organisation = "organisation";
    public const string Person = "person";

    public static readonly IReadOnlyList<string> All = [Organisation, Person];
}

/// <summary>Public facts about a contact other modules may use (a sales order's customer, a
/// purchase order's supplier): the names to print, how to reach them and whether it is in use.</summary>
public sealed record ContactInfo(
    Guid Id,
    string Kind,
    string? Reference,
    string NameEn,
    string NameAr,
    Guid? OrganisationId,
    string? Email,
    string? Phone,
    string? Mobile,
    string? TaxRegistrationNumber,
    string DefaultCurrency,
    bool IsActive);

/// <summary>Reads contacts of the current workspace (row-level security, the session's tenant).
/// Other modules use this instead of the contacts tables.</summary>
public interface IContactDirectory
{
    /// <summary>A contact of the workspace, or null (also for another workspace's id).</summary>
    Task<ContactInfo?> GetAsync(Guid contactId, CancellationToken cancellationToken);
}
