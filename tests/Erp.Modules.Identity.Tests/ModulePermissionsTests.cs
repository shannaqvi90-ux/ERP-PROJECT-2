using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Erp.Modules.Identity.Tests;

/// <summary>
/// A module of a later wave, as the identity module will meet it: it brings its own permissions
/// (view, create, change, delete parcels) and endpoints, and nothing in identity changes. Hosted
/// only by these tests, the way a real module is hosted by the product.
/// </summary>
public sealed class LaterMailroomModule : ErpModule
{
    public override string Name => "mailroom";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions("mailroom.parcels.read", "mailroom.parcels.create", "mailroom.parcels.update", "mailroom.parcels.delete");
        module.Endpoints(group =>
        {
            group.MapGet("/parcels", () => Results.Ok(new { items = Array.Empty<object>() }))
                .WithName("mailroom.parcels.list").WithSummary("Parcels.").RequirePermission("mailroom.parcels.read");
            group.MapPost("/parcels", () => Results.Created("/api/mailroom/parcels/1", new { id = Guid.NewGuid() }))
                .WithName("mailroom.parcels.create").WithSummary("Create a parcel.").RequirePermission("mailroom.parcels.create");
            group.MapPut("/parcels/{id:guid}", (Guid id) => Results.Ok(new { id }))
                .WithName("mailroom.parcels.update").WithSummary("Change a parcel.").RequirePermission("mailroom.parcels.update");
            group.MapDelete("/parcels/{id:guid}", (Guid id) => Results.NoContent())
                .WithName("mailroom.parcels.delete").WithSummary("Delete a parcel.").RequirePermission("mailroom.parcels.delete");
        });
    }
}

public sealed class LaterModuleFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?>
    {
        ["Erp:Testing:ExtraModules"] = typeof(LaterMailroomModule).AssemblyQualifiedName,
    });

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// Roles are ready for module permissions that do not exist yet: the bar's "create a user with a
/// restricted role" asks for someone who may view and create parcels and do nothing else. With a
/// mailroom module present, that role is one POST, the user holds exactly those two permissions in
/// the API, the access view explains them, and the permission catalogue offers them in the matrix
/// under the module's own block. The Administrator role follows the catalogue.
/// </summary>
public sealed class ModulePermissionsTests(LaterModuleFixture fixture) : IClassFixture<LaterModuleFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task A_role_that_may_only_view_and_create_parcels_works_end_to_end()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));

        // The Administrator role holds the new module's permissions (kept in step with the catalogue).
        var administrator = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean());
        Assert.Contains("mailroom.parcels.create", administrator.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        // The catalogue offers the module's permissions with their resource and action, for the matrix.
        var catalogue = await admin.GetFromJsonAsync<JsonElement>("/api/identity/permissions");
        var contacts = catalogue.EnumerateArray().Where(p => p.GetProperty("module").GetString() == "mailroom").ToList();
        Assert.Equal(["create", "delete", "read", "update"], contacts.Select(p => p.GetProperty("action").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(contacts, p => Assert.Equal("parcels", p.GetProperty("resource").GetString()));

        var role = await admin.PostAsJsonAsync("/api/identity/roles",
            new { nameEn = "Parcels clerk", nameAr = "كاتب الطرود", permissions = new[] { "mailroom.parcels.read", "mailroom.parcels.create" } });
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var email = $"hessa.clerk@{Env.TenantA.EmailDomain}";
        var user = await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = "Hessa Clerk", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // In the API: view and create parcels, and nothing else.
        using var clerk = await Env.SignInAsync(email);
        Assert.Equal(["mailroom.parcels.create", "mailroom.parcels.read"],
            (await clerk.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await clerk.GetAsync("/api/mailroom/parcels")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await clerk.PostAsJsonAsync("/api/mailroom/parcels", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PutAsJsonAsync($"/api/mailroom/parcels/{Guid.NewGuid()}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.DeleteAsync($"/api/mailroom/parcels/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync("/api/identity/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync("/api/identity/roles")).StatusCode);

        // The access view explains it: two permissions, both from the clerk role.
        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{userId}/access");
        var held = access.GetProperty("permissions").EnumerateArray().ToList();
        Assert.Equal(["mailroom.parcels.create", "mailroom.parcels.read"], held.Select(p => p.GetProperty("key").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(held, p => Assert.Equal(roleId, p.GetProperty("grantedBy")[0].GetGuid()));

        // A user who may manage users but not parcels cannot hand out the clerk role.
        var userAdmin = await admin.PostAsJsonAsync("/api/identity/roles",
            new { nameEn = "User desk", nameAr = "مكتب المستخدمين", permissions = new[] { "identity.users.read", "identity.users.create", "identity.roles.read" } });
        var deskEmail = $"desk@{Env.TenantA.EmailDomain}";
        await admin.PostAsJsonAsync("/api/identity/users",
            new { email = deskEmail, displayName = "Desk", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { (await userAdmin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid() } });
        using var desk = await Env.SignInAsync(deskEmail);
        var minted = await desk.PostAsJsonAsync("/api/identity/users",
            new { email = $"minted@{Env.TenantA.EmailDomain}", displayName = "Minted", language = "en", roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Forbidden, minted.StatusCode);
    }
}
