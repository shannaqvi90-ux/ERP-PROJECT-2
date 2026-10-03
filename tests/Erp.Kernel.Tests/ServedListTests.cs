using Erp.Kernel.Hosting;
using Erp.Kernel.Lists;
using Erp.Kernel.Localization;
using Erp.Kernel.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Kernel.Tests;

/// <summary>A module's list whose rows belong to another module: the other module's list binding
/// serves it (<see cref="ModuleBuilder.List(ListDefinition, string)"/>), whatever order the two
/// modules register in, and only for columns it binds.</summary>
public sealed class ServedListTests
{
    public sealed record Person(Guid Id, string Name, string Email, int Age);

    private static readonly ListDefinition People = new(
        "people.all", "people.all.title", "people.all.read", "/api/people",
        [
            new ListColumn("name", "people.name", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("email", "people.email", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("age", "people.age", ListColumnType.Number, Sortable: true, Filterable: true),
        ],
        SearchFields: ["name", "email"],
        DefaultSort: "name");

    private static ListBinding<Person> PeopleBinding() => ListBinding<Person>.For(People, p => p.Id)
        .Column("name", p => p.Name)
        .Column("email", p => p.Email)
        .Column("age", p => p.Age)
        .InMemory("test rows");

    private static ListDefinition Members(params ListColumn[] extra) => new(
        "club.members", "club.members.title", "club.members.read", "/api/club/members",
        [
            new ListColumn("name", "club.name", ListColumnType.Text, Sortable: true, Filterable: true),
            new ListColumn("email", "club.email", ListColumnType.Text, Sortable: true),
            new ListColumn("teams", "club.teams", ListColumnType.Reference),
            .. extra,
        ],
        SearchFields: ["name", "email"],
        DefaultSort: "name");

    private static readonly List<Person> Rows = Enumerable.Range(0, 12)
        .Select(i => new Person(Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), $"Person {(char)('A' + i)}", $"p{i}@example.ae", 20 + i)).ToList();

    private static HttpContext Http()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new StringCatalog([typeof(ErpPlatform).Assembly]));
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static ModuleCatalog Register(params ErpModule[] modules)
    {
        var host = WebApplication.CreateSlimBuilder();
        host.Configuration["ConnectionStrings:App"] = "Host=localhost;Username=erp_app;Password=x;Database=erp";
        host.AddErpPlatform(modules);
        return (ModuleCatalog)host.Services.Single(s => s.ServiceType == typeof(ModuleCatalog)).ImplementationInstance!;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_serving_list_lends_its_binding_whatever_the_registration_order(bool servingFirst)
    {
        var people = new PeopleModule();
        var club = new ClubModule(Members());
        var catalog = servingFirst ? Register(people, club) : Register(club, people);

        var binding = catalog.ListBinding<Person>("club.members");
        Assert.Equal("club.members", binding.Definition.Key);
        Assert.Equal("club.members.read", binding.Definition.Permission);
        // Only the columns the served list names keep their bindings.
        Assert.Equal(["email", "name"], binding.Columns.Select(c => c.Key).Order());
        Assert.Contains(catalog.ListBindings, b => b.Definition.Key == "club.members");

        var page = await binding.QueryAsync(Rows.AsQueryable(), new ListRequest { Sort = "-name", Take = 5 }, Http(), CancellationToken.None);
        Assert.Null(page.Problem);
        Assert.Equal(Rows.OrderByDescending(r => r.Name, StringComparer.Ordinal).Take(5).Select(r => r.Id), page.Rows.Select(r => r.Id));
        Assert.Equal(12, page.Total);
        Assert.NotNull(page.Next);

        // The served list's own definition decides: email is not filterable there, age is not a column.
        var refused = await binding.QueryAsync(Rows.AsQueryable(), new ListRequest { Filter = "email eq 'p1@example.ae'" }, Http(), CancellationToken.None);
        Assert.IsType<ProblemHttpResult>(refused.Problem);
        var unknown = await binding.QueryAsync(Rows.AsQueryable(), new ListRequest { Sort = "age" }, Http(), CancellationToken.None);
        Assert.IsType<ProblemHttpResult>(unknown.Problem);

        // A result handed through a contract keeps its page or its problem.
        var mapped = page.Map(p => p.Email);
        Assert.Equal(page.Rows.Select(r => r.Email), mapped.Rows);
        Assert.Equal(page.Next, mapped.Next);
        Assert.Same(refused.Problem, refused.Map(p => p.Email).Problem);
    }

    [Fact]
    public void A_served_list_that_needs_a_column_the_serving_list_does_not_bind_is_refused()
    {
        var club = new ClubModule(Members(new ListColumn("joinedAt", "club.joined", ListColumnType.DateTime, Sortable: true)));
        var refused = Assert.Throws<InvalidOperationException>(() => Register(new PeopleModule(), club));
        Assert.Contains("column 'joinedAt' is sortable, filterable, groupable, totalled or searched but not bound", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_served_list_must_name_another_list()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration["ConnectionStrings:App"] = "Host=localhost;Username=erp_app;Password=x;Database=erp";
        var refused = Assert.Throws<InvalidOperationException>(() => builder.AddErpPlatform([new ClubModule(Members(), servedBy: "club.members")]));
        Assert.Contains("names no other list", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_served_list_whose_serving_list_is_missing_stays_without_a_binding()
    {
        var catalog = Register(new ClubModule(Members()));
        Assert.DoesNotContain(catalog.ListBindings, b => b.Definition.Key == "club.members");
        Assert.Equal("people.all", catalog.Modules.Single().ListsServedBy["club.members"]);
    }

    private sealed class PeopleModule : ErpModule
    {
        public override string Name => "people";

        public override void Register(ModuleBuilder module)
        {
            module.Permissions("people.all.read");
            module.List(PeopleBinding());
        }
    }

    private sealed class ClubModule(ListDefinition members, string servedBy = "people.all") : ErpModule
    {
        public override string Name => "club";

        public override void Register(ModuleBuilder module)
        {
            module.Permissions("club.members.read");
            module.List(members, servedBy);
        }
    }
}
