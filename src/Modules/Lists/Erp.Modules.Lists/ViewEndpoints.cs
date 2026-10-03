using Erp.Kernel.Http;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Modules.Lists.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Lists;

public sealed record ListChoiceDto(string Value, string LabelKey);

public sealed record ListColumnDto(
    string Key, string LabelKey, ListColumnType Type, bool Sortable, bool Filterable, bool Groupable, bool Aggregate, bool Hidden,
    IReadOnlyList<ListChoiceDto> Choices, IReadOnlyList<FilterOperator> Operators);

public sealed record ListPresetDto(string Key, string LabelKey, string? Filter, string? Sort, string? GroupBy);

/// <summary>What a screen needs to show a registered list: its columns with their types, labels
/// and abilities, the search fields, default sort, built-in views, and whether the caller may
/// share views.</summary>
public sealed record ListDefinitionDto(
    string Key, string LabelKey, string Endpoint, IReadOnlyList<ListColumnDto> Columns, IReadOnlyList<string> SearchFields,
    string? DefaultSort, IReadOnlyList<ListPresetDto> Presets, bool CanShare, int MaxTake);

public sealed record SavedViewDto(
    Guid Id, string ListKey, string Name, bool IsShared, bool IsDefault, bool IsMine, IReadOnlyList<string> Columns,
    string? Sort, string? Filter, string? Search, string? GroupBy, DateTimeOffset UpdatedAt, uint Version);

/// <param name="Name">The view's name, as the user wrote it (1 to 100 characters).</param>
/// <param name="Columns">Visible column keys in display order (at least one).</param>
/// <param name="Sort">Sort in the list query form, or null for the list's default.</param>
/// <param name="Filter">Filter expression, or null.</param>
/// <param name="Search">Search words, or null.</param>
/// <param name="GroupBy">Groupable column key, or null.</param>
/// <param name="IsDefault">Open this view when the list opens (replaces the previous default).</param>
/// <param name="Version">The version read (required to change a view).</param>
public sealed record SaveViewRequest(
    string? Name, IReadOnlyList<string>? Columns, string? Sort, string? Filter, string? Search, string? GroupBy, bool? IsDefault, uint? Version);

/// <summary>
/// Endpoints of every registered list under <c>/api/lists/{list key}</c>: its definition and
/// saved views. Each list's endpoints declare that list's read permission, so whoever can read a
/// list can see its definition and keep personal views of it, and nobody else can; writing
/// shared views declares <see cref="ListsPermissions.ViewsShare"/> and also needs the list's
/// permission (a list the caller cannot read does not exist for them: 404).
/// </summary>
internal static class ViewEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var catalog = ((IEndpointRouteBuilder)group).ServiceProvider.GetRequiredService<ModuleCatalog>();
        foreach (var list in catalog.Lists)
        {
            var key = list.Key;
            var routes = group.MapGroup("/" + key);
            var name = $"lists.{key}";

            routes.MapGet("/definition", (ModuleCatalog c, ICurrentUser caller) => TypedResults.Ok(Definition(c.FindList(key)!, caller)))
                .WithName($"{name}.definition")
                .WithSummary($"Definition of the {key} list: columns, types, labels, operators, search fields, default sort, built-in views.")
                .RequirePermission(list.Permission);

            routes.MapGet("/views", (ListsDbContext db, ICurrentUser caller, CancellationToken ct) => Views(key, db, caller, ct))
                .WithName($"{name}.views.list")
                .WithSummary($"Saved views of the {key} list the caller can use: shared views, then the caller's own.")
                .RequirePermission(list.Permission);

            routes.MapGet("/views/{id:guid}", (Guid id, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) => GetOwn(key, id, db, caller, http, ct))
                .WithName($"{name}.views.get")
                .WithSummary($"One of the caller's own saved views of the {key} list.")
                .RequirePermission(list.Permission);

            routes.MapPost("/views", (SaveViewRequest request, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Create(c.FindList(key)!, request, shared: false, db, caller, http, ct))
                .WithName($"{name}.views.create")
                .WithSummary($"Save a personal view of the {key} list.")
                .ProducesValidationProblem()
                .RequirePermission(list.Permission);

            routes.MapPut("/views/{id:guid}", (Guid id, SaveViewRequest request, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Update(c.FindList(key)!, id, request, shared: false, db, caller, http, ct))
                .WithName($"{name}.views.update")
                .WithSummary($"Change one of the caller's own views of the {key} list.")
                .ProducesValidationProblem()
                .RequirePermission(list.Permission);

            routes.MapDelete("/views/{id:guid}", (Guid id, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Delete(c.FindList(key)!, id, shared: false, db, caller, http, ct))
                .WithName($"{name}.views.delete")
                .WithSummary($"Delete one of the caller's own views of the {key} list.")
                .RequirePermission(list.Permission);

            routes.MapGet("/shared-views/{id:guid}", (Guid id, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) => GetShared(key, id, db, caller, http, ct))
                .WithName($"{name}.sharedViews.get")
                .WithSummary($"One shared view of the {key} list.")
                .RequirePermission(list.Permission);

            routes.MapPost("/shared-views", (SaveViewRequest request, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Create(c.FindList(key)!, request, shared: true, db, caller, http, ct))
                .WithName($"{name}.sharedViews.create")
                .WithSummary($"Share a view of the {key} list with everyone who can read it (also needs the list's read permission).")
                .ProducesValidationProblem()
                .RequirePermission(ListsPermissions.ViewsShare);

            routes.MapPut("/shared-views/{id:guid}", (Guid id, SaveViewRequest request, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Update(c.FindList(key)!, id, request, shared: true, db, caller, http, ct))
                .WithName($"{name}.sharedViews.update")
                .WithSummary($"Change a shared view of the {key} list (also needs the list's read permission).")
                .ProducesValidationProblem()
                .RequirePermission(ListsPermissions.ViewsShare);

            routes.MapDelete("/shared-views/{id:guid}", (Guid id, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Delete(c.FindList(key)!, id, shared: true, db, caller, http, ct))
                .WithName($"{name}.sharedViews.delete")
                .WithSummary($"Delete a shared view of the {key} list (also needs the list's read permission).")
                .RequirePermission(ListsPermissions.ViewsShare);
        }
    }

    internal static ListDefinitionDto Definition(ListDefinition list, ICurrentUser caller) => new(
        list.Key,
        list.LabelKey,
        list.Endpoint,
        list.Columns.Select(c => new ListColumnDto(
            c.Key, c.LabelKey, c.Type, c.Sortable, c.Filterable, c.Groupable, c.Aggregate, c.Hidden,
            (c.Choices ?? []).Select(x => new ListChoiceDto(x.Value, x.LabelKey)).ToList(),
            c.Filterable ? ListFilter.Allowed(c.Type) : [])).ToList(),
        list.SearchFields,
        list.DefaultSort,
        (list.Presets ?? []).Select(p => new ListPresetDto(p.Key, p.LabelKey, p.Filter, p.Sort, p.GroupBy)).ToList(),
        caller.Has(ListsPermissions.ViewsShare),
        ListRequest.MaxTake);

    private static async Task<Ok<ListPage<SavedViewDto>>> Views(string list, ListsDbContext db, ICurrentUser caller, CancellationToken cancellationToken)
    {
        var me = caller.UserId;
        var views = await db.SavedViews.AsNoTracking()
            .Where(v => v.ListKey == list && (v.IsShared || v.OwnerUserId == me))
            .OrderBy(v => v.IsShared ? 0 : 1).ThenBy(v => v.Name).ThenBy(v => v.Id)
            .ToListAsync(cancellationToken);
        var items = views.Select(v => ToDto(v, me)).ToList();
        return TypedResults.Ok(new ListPage<SavedViewDto>(items, items.Count));
    }

    private static async Task<Results<Ok<SavedViewDto>, ProblemHttpResult>> GetOwn(string list, Guid id, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var me = caller.UserId;
        var view = await db.SavedViews.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id && v.ListKey == list && !v.IsShared && v.OwnerUserId == me, cancellationToken);
        return view is null ? Problems.NotFound(http) : TypedResults.Ok(ToDto(view, me));
    }

    private static async Task<Results<Ok<SavedViewDto>, ProblemHttpResult>> GetShared(string list, Guid id, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var view = await db.SavedViews.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id && v.ListKey == list && v.IsShared, cancellationToken);
        return view is null ? Problems.NotFound(http) : TypedResults.Ok(ToDto(view, caller.UserId));
    }

    private static async Task<Results<Created<SavedViewDto>, ProblemHttpResult>> Create(
        ListDefinition list, SaveViewRequest request, bool shared, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(list, request, http, requireVersion: false);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!caller.Has(list.Permission))
        {
            return Problems.NotFound(http);
        }
        var owner = shared ? (Guid?)null : caller.UserId;
        var name = request.Name!.Trim();
        if (await db.SavedViews.AnyAsync(v => v.ListKey == list.Key && v.IsShared == shared && v.OwnerUserId == owner && v.Name == name, cancellationToken))
        {
            return Problems.Conflict(http, "lists.viewNameTaken");
        }
        if (request.IsDefault == true)
        {
            await ClearOtherDefaultsAsync(list.Key, shared, owner, null, db, cancellationToken);
        }
        var view = new SavedView { ListKey = list.Key, IsShared = shared, OwnerUserId = owner };
        Apply(view, request);
        db.SavedViews.Add(view);
        await db.SaveChangesAsync(cancellationToken);
        var path = shared ? "shared-views" : "views";
        return TypedResults.Created($"/api/lists/{list.Key}/{path}/{view.Id}", ToDto(view, caller.UserId));
    }

    private static async Task<Results<Ok<SavedViewDto>, ProblemHttpResult>> Update(
        ListDefinition list, Guid id, SaveViewRequest request, bool shared, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        var validator = Validate(list, request, http, requireVersion: true);
        if (!validator.IsValid)
        {
            return validator.ToResult();
        }
        if (!caller.Has(list.Permission))
        {
            return Problems.NotFound(http);
        }
        var owner = shared ? (Guid?)null : caller.UserId;
        var view = await db.SavedViews.SingleOrDefaultAsync(v => v.Id == id && v.ListKey == list.Key && v.IsShared == shared && v.OwnerUserId == owner, cancellationToken);
        if (view is null)
        {
            return Problems.NotFound(http);
        }
        var name = request.Name!.Trim();
        if (await db.SavedViews.AnyAsync(v => v.Id != id && v.ListKey == list.Key && v.IsShared == shared && v.OwnerUserId == owner && v.Name == name, cancellationToken))
        {
            return Problems.Conflict(http, "lists.viewNameTaken");
        }
        if (view.Version != request.Version!.Value)
        {
            return Problems.Conflict(http, "concurrency");
        }
        if (request.IsDefault == true)
        {
            await ClearOtherDefaultsAsync(list.Key, shared, owner, view.Id, db, cancellationToken);
        }
        db.Entry(view).Property(v => v.Version).OriginalValue = request.Version!.Value;
        Apply(view, request);
        db.Entry(view).Property(v => v.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(view, caller.UserId));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        ListDefinition list, Guid id, bool shared, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken cancellationToken)
    {
        if (!caller.Has(list.Permission))
        {
            return Problems.NotFound(http);
        }
        var owner = shared ? (Guid?)null : caller.UserId;
        var view = await db.SavedViews.SingleOrDefaultAsync(v => v.Id == id && v.ListKey == list.Key && v.IsShared == shared && v.OwnerUserId == owner, cancellationToken);
        if (view is null)
        {
            return Problems.NotFound(http);
        }
        db.SavedViews.Remove(view);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>A new default replaces the previous one of the same owner (or among shared views).
    /// The previous default is saved first, so the one-default index never sees two.</summary>
    private static async Task ClearOtherDefaultsAsync(string list, bool shared, Guid? owner, Guid? except, ListsDbContext db, CancellationToken cancellationToken)
    {
        var previous = await db.SavedViews
            .Where(v => v.ListKey == list && v.IsShared == shared && v.OwnerUserId == owner && v.IsDefault && v.Id != except)
            .ToListAsync(cancellationToken);
        if (previous.Count == 0)
        {
            return;
        }
        foreach (var old in previous)
        {
            old.IsDefault = false;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Apply(SavedView view, SaveViewRequest request)
    {
        view.Name = request.Name!.Trim();
        view.Columns = request.Columns!.ToList();
        view.Sort = Blank(request.Sort);
        view.Filter = Blank(request.Filter);
        view.Search = Blank(request.Search);
        view.GroupBy = Blank(request.GroupBy);
        view.IsDefault = request.IsDefault ?? false;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Validator Validate(ListDefinition list, SaveViewRequest request, HttpContext http, bool requireVersion)
    {
        var validator = new Validator(http)
            .Required("name", request.Name).MaxLength("name", request.Name?.Trim(), ListsDbContext.NameMaxLength)
            .Must(request.Columns is { Count: > 0 }, "columns", "required");
        if (request.Columns is { Count: > 0 } columns)
        {
            validator.Must(columns.All(c => list.Column(c) is not null) && columns.Distinct(StringComparer.Ordinal).Count() == columns.Count,
                "columns", "unknownIds");
        }
        Check(validator, () =>
        {
            if (Blank(request.Sort) is { } sort)
            {
                ListSortKey.Parse(sort, list);
            }
        });
        Check(validator, () =>
        {
            if (Blank(request.Filter) is { } filter)
            {
                ListFilter.Validate(ListFilter.Parse(filter), list);
            }
        });
        Check(validator, () =>
        {
            if (Blank(request.Search) is { } search)
            {
                if (search.Length > ListRequest.MaxSearchLength)
                {
                    throw new ListQueryException("search", "maxLength", ListRequest.MaxSearchLength);
                }
                if (search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > ListRequest.MaxSearchWords)
                {
                    throw new ListQueryException("search", "list.searchTooManyWords", ListRequest.MaxSearchWords);
                }
                if (list.SearchFields.Count == 0)
                {
                    throw new ListQueryException("search", "list.notSearchable");
                }
            }
        });
        Check(validator, () =>
        {
            if (Blank(request.GroupBy) is { } group)
            {
                if (list.Column(group) is not { } column)
                {
                    throw new ListQueryException("groupBy", "list.unknownColumn");
                }
                if (!column.Groupable)
                {
                    throw new ListQueryException("groupBy", "list.notGroupable", column.Key);
                }
            }
        });
        if (requireVersion)
        {
            validator.Required("version", request.Version);
        }
        return validator;
    }

    private static void Check(Validator validator, Action check)
    {
        try
        {
            check();
        }
        catch (ListQueryException e)
        {
            validator.Add(e.Parameter, e.Code, [.. e.Args]);
        }
    }

    private static SavedViewDto ToDto(SavedView v, Guid me) => new(
        v.Id, v.ListKey, v.Name, v.IsShared, v.IsDefault, !v.IsShared && v.OwnerUserId == me, v.Columns, v.Sort, v.Filter, v.Search, v.GroupBy, v.UpdatedAt, v.Version);
}
