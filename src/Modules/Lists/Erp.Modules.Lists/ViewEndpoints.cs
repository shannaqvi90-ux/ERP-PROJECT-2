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
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Erp.Modules.Lists;

public sealed record ListChoiceDto(string Value, string LabelKey);

public sealed record ListColumnDto(
    string Key, string LabelKey, ListColumnType Type, bool Sortable, bool Filterable, bool Groupable, bool Aggregate, bool Hidden,
    IReadOnlyList<ListChoiceDto> Choices, IReadOnlyList<FilterOperator> Operators, string? CurrencyField = null);

public sealed record ListPresetDto(string Key, string LabelKey, string? Filter, string? Sort, string? GroupBy);

/// <summary>What a screen needs to show a registered list: its columns with their types, labels
/// and abilities, the search fields, default sort, built-in views, and whether the caller may
/// share views.</summary>
public sealed record ListDefinitionDto(
    string Key, string LabelKey, string Endpoint, IReadOnlyList<ListColumnDto> Columns, IReadOnlyList<string> SearchFields,
    string? DefaultSort, IReadOnlyList<ListPresetDto> Presets, bool CanShare, int MaxTake, bool Printable = false,
    IReadOnlyList<string>? ArabicSearchFields = null);

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

            routes.MapGet("/definition", (ModuleCatalog c, ICurrentUser caller) =>
                    TypedResults.Ok(Definition(c.FindList(key)!, caller, c.PrintableLists.Any(p => p.List.Key == key))))
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
                .WithViewSchema(list)
                .WithSummary($"Save a personal view of the {key} list.")
                .ProducesValidationProblem()
                .RequirePermission(list.Permission);

            routes.MapPut("/views/{id:guid}", (Guid id, SaveViewRequest request, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Update(c.FindList(key)!, id, request, shared: false, db, caller, http, ct))
                .WithName($"{name}.views.update")
                .WithViewSchema(list)
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
                .AddEndpointFilter(AlsoRequires(list.Permission))
                .WithViewSchema(list)
                .WithSummary($"Share a view of the {key} list with everyone who can read it (also needs the list's read permission).")
                .ProducesValidationProblem()
                .RequirePermission(ListsPermissions.ViewsShare);

            routes.MapPut("/shared-views/{id:guid}", (Guid id, SaveViewRequest request, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Update(c.FindList(key)!, id, request, shared: true, db, caller, http, ct))
                .WithName($"{name}.sharedViews.update")
                .AddEndpointFilter(AlsoRequires(list.Permission))
                .WithViewSchema(list)
                .WithSummary($"Change a shared view of the {key} list (also needs the list's read permission).")
                .ProducesValidationProblem()
                .RequirePermission(ListsPermissions.ViewsShare);

            routes.MapDelete("/shared-views/{id:guid}", (Guid id, ModuleCatalog c, ListsDbContext db, ICurrentUser caller, HttpContext http, CancellationToken ct) =>
                    Delete(c.FindList(key)!, id, shared: true, db, caller, http, ct))
                .WithName($"{name}.sharedViews.delete")
                .AddEndpointFilter(AlsoRequires(list.Permission))
                .WithSummary($"Delete a shared view of the {key} list (also needs the list's read permission).")
                .RequirePermission(ListsPermissions.ViewsShare);
        }
    }

    /// <summary>Shared-view writes declare the share permission; they also need the list's own
    /// permission. Checked before the handler runs (a list the caller cannot read does not exist
    /// for them: 404), and again inside the handler.</summary>
    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> AlsoRequires(string permission) =>
        async (context, next) =>
        {
            var caller = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            return caller.Has(permission) ? await next(context) : Problems.NotFound(context.HttpContext);
        };

    /// <summary>Document the request body of a view endpoint for this list: the column keys and
    /// groupable columns it accepts as enums, a valid sort and filter as examples.</summary>
    private static RouteHandlerBuilder WithViewSchema(this RouteHandlerBuilder builder, ListDefinition list) =>
        builder.AddOpenApiOperationTransformer((operation, _, _) =>
        {
            if (operation.RequestBody?.Content is { } content && content.TryGetValue("application/json", out var media))
            {
                media.Schema = ViewSchema(list);
            }
            return Task.CompletedTask;
        });

    private static OpenApiSchema ViewSchema(ListDefinition list)
    {
        static JsonNode Text(string value) => JsonValue.Create(value);
        static IList<JsonNode>? Example(string? value) => value is null ? null : [Text(value)];
        var groupable = list.Columns.Where(c => c.Groupable).Select(c => Text(c.Key)).ToList();
        var (sort, filter, _) = ViewExample.For(list);
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = $"A view of the {list.Key} list.",
            Required = new HashSet<string> { "name", "columns" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["name"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = ListsDbContext.NameMaxLength, Description = "The view's name, 1 to 100 characters." },
                ["columns"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    MinItems = 1,
                    Description = "Visible column keys in display order.",
                    Items = new OpenApiSchema { Type = JsonSchemaType.String, Enum = list.Columns.Select(c => Text(c.Key)).ToList() },
                },
                ["sort"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Examples = Example(sort), Description = "Sort as in the list's sort parameter; null for the default." },
                ["filter"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, MaxLength = ListFilter.MaxLength, Examples = Example(filter), Description = "Filter expression as in the list's filter parameter." },
                ["search"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, MaxLength = ListRequest.MaxSearchLength, Description = "Search words." },
                ["groupBy"] = groupable.Count > 0
                    ? new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null, Enum = groupable, Description = "A groupable column key, or null." }
                    : new OpenApiSchema { Type = JsonSchemaType.Null, Description = "The list has no groupable columns." },
                ["isDefault"] = new OpenApiSchema { Type = JsonSchemaType.Boolean | JsonSchemaType.Null, Description = "Open this view when the list opens." },
                ["version"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int64", Description = "The version read; required to change a view." },
            },
        };
    }

    internal static ListDefinitionDto Definition(ListDefinition list, ICurrentUser caller, bool printable = false) => new(
        list.Key,
        list.LabelKey,
        list.Endpoint,
        list.Columns.Select(c => new ListColumnDto(
            c.Key, c.LabelKey, c.Type, c.Sortable, c.Filterable, c.Groupable, c.Aggregate, c.Hidden,
            (c.Choices ?? []).Select(x => new ListChoiceDto(x.Value, x.LabelKey)).ToList(),
            c.Filterable ? ListFilter.Allowed(c.Type) : [], c.CurrencyField)).ToList(),
        list.SearchFields,
        list.DefaultSort,
        (list.Presets ?? []).Select(p => new ListPresetDto(p.Key, p.LabelKey, p.Filter, p.Sort, p.GroupBy)).ToList(),
        caller.Has(ListsPermissions.ViewsShare),
        ListRequest.MaxTake,
        Printable: printable,
        ArabicSearchFields: list.ArabicSearchFields is { Count: > 0 } arabic ? arabic : list.SearchFields);

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

/// <summary>A valid sort, filter and grouping for a list: the API document's examples for view
/// bodies, and the seeded team view (so a workspace starts with a view that shows them).</summary>
internal static class ViewExample
{
    public static (string? Sort, string? Filter, string? GroupBy) For(ListDefinition list) => (
        list.DefaultSort ?? list.Columns.FirstOrDefault(c => c.Sortable)?.Key,
        list.Presets?.FirstOrDefault(p => p.Filter is not null)?.Filter
            ?? (list.Columns.FirstOrDefault(c => c.Filterable) is { } filterable ? $"{filterable.Key} is not null" : null),
        list.Columns.FirstOrDefault(c => c.Groupable)?.Key);
}
