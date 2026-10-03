namespace Erp.Modules.Lists.Contracts;

public static class ListsPermissions
{
    /// <summary>Create, change and delete views every user of a list sees (on top of the list's
    /// own read permission, which personal views need).</summary>
    public const string ViewsShare = "lists.views.share";

    public static readonly IReadOnlyList<string> All = [ViewsShare];
}
