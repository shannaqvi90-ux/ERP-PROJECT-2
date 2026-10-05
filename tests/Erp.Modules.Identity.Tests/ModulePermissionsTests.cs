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
/// (view, create, change, delete contacts) and endpoints, and nothing in identity changes. Hosted
/// only by these tests, the way the contacts directory (p16) will be hosted by the product.
/// </summary>
public sealed class LaterContactsModule : ErpModule
{
    public override string Name => "contacts";

    public override void Register(ModuleBuilder module)
    {
        module.Permissions("contacts.contacts.read", "contacts.contacts.create", "contacts.contacts.update", "contacts.contacts.delete");
        module.Endpoints(group =>
        {
            group.MapGet("/contacts", () => Results.Ok(new { items = Array.Empty<object>() }))
                .WithName("contacts.contacts.list").WithSummary("Contacts.").RequirePermission("contacts.contacts.read");
            group.MapPost("/contacts", () => Results.Created("/api/contacts/contacts/1", new { id = Guid.NewGuid() }))
                .WithName("contacts.contacts.create").WithSummary("Create a contact.").RequirePermission("contacts.contacts.create");
            group.MapPut("/contacts/{id:guid}", (Guid id) => Results.Ok(new { id }))
                .WithName("contacts.contacts.update").WithSummary("Change a contact.").RequirePermission("contacts.contacts.update");
            group.MapDelete("/contacts/{id:guid}", (Guid id) => Results.NoContent())
                .WithName("contacts.contacts.delete").WithSummary("Delete a contact.").RequirePermission("contacts.contacts.delete");
        });
    }
}

public sealed class LaterModuleFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?>
    {
        ["Erp:Testing:ExtraModules"] = typeof(LaterContactsModule).AssemblyQualifiedName,
    });

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();
}

/// <summary>
/// Roles are ready for module permissions that do not exist yet: the bar's "create a user with a
/// restricted role" asks for someone who may view and create contacts and do nothing else. With a
/// contacts module present, that role is one POST, the user holds exactly those two permissions in
/// the API, the access view explains them, and the permission catalogue offers them in the matrix
/// under the module's own block. The Administrator role follows the catalogue.
/// </summary>
public sealed class ModulePermissionsTests(LaterModuleFixture fixture) : IClassFixture<LaterModuleFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    [Fact]
    public async Task A_role_that_may_only_view_and_create_contacts_works_end_to_end()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));

        // The Administrator role holds the new module's permissions (kept in step with the catalogue).
        var administrator = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("isSystem").GetBoolean());
        Assert.Contains("contacts.contacts.create", administrator.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        // The catalogue offers the module's permissions with their resource and action, for the matrix.
        var catalogue = await admin.GetFromJsonAsync<JsonElement>("/api/identity/permissions");
        var contacts = catalogue.EnumerateArray().Where(p => p.GetProperty("module").GetString() == "contacts").ToList();
        Assert.Equal(["create", "delete", "read", "update"], contacts.Select(p => p.GetProperty("action").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(contacts, p => Assert.Equal("contacts", p.GetProperty("resource").GetString()));

        var role = await admin.PostAsJsonAsync("/api/identity/roles",
            new { nameEn = "Contacts clerk", nameAr = "كاتب جهات الاتصال", permissions = new[] { "contacts.contacts.read", "contacts.contacts.create" } });
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var email = $"hessa.clerk@{Env.TenantA.EmailDomain}";
        var user = await admin.PostAsJsonAsync("/api/identity/users",
            new { email, displayName = "Hessa Clerk", language = "en", password = ErpTestEnvironment.Password, mustChangePassword = false, roleIds = new[] { roleId } });
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        var userId = (await user.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // In the API: view and create contacts, and nothing else.
        using var clerk = await Env.SignInAsync(email);
        Assert.Equal(["contacts.contacts.create", "contacts.contacts.read"],
            (await clerk.GetFromJsonAsync<JsonElement>("/api/auth/session")).GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await clerk.GetAsync("/api/contacts/contacts")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await clerk.PostAsJsonAsync("/api/contacts/contacts", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PutAsJsonAsync($"/api/contacts/contacts/{Guid.NewGuid()}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.DeleteAsync($"/api/contacts/contacts/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync("/api/identity/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync("/api/identity/roles")).StatusCode);

        // The access view explains it: two permissions, both from the clerk role.
        var access = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/users/{userId}/access");
        var held = access.GetProperty("permissions").EnumerateArray().ToList();
        Assert.Equal(["contacts.contacts.create", "contacts.contacts.read"], held.Select(p => p.GetProperty("key").GetString()!).Order(StringComparer.Ordinal));
        Assert.All(held, p => Assert.Equal(roleId, p.GetProperty("grantedBy")[0].GetGuid()));

        // A user who may manage users but not contacts cannot hand out the clerk role.
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
