using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.Identity.Tests;

/// <summary>User and role administration through the API: validation in both languages,
/// optimistic concurrency, duplicates, system roles and personal preferences.</summary>
public sealed class UsersAndRolesTests(IdentityFixture fixture) : IClassFixture<IdentityFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Validation_errors_name_each_field_in_the_requested_language()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/users")
        {
            Content = JsonContent.Create(new { email = "not-an-email", displayName = "", language = "fr", password = "short" }),
        };
        request.Headers.AcceptLanguage.ParseAdd("ar");
        var response = await admin.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("validation", problem.GetProperty("code").GetString());
        var errors = problem.GetProperty("errors");
        Assert.Equal("email", errors.GetProperty("email")[0].GetProperty("code").GetString());
        Assert.Equal("required", errors.GetProperty("displayName")[0].GetProperty("code").GetString());
        Assert.Equal("oneOf", errors.GetProperty("language")[0].GetProperty("code").GetString());
        Assert.Equal("passwordTooShort", errors.GetProperty("password")[0].GetProperty("code").GetString());
        // The signed-in administrator prefers English, so the user's preference wins over the header.
        Assert.Equal("Some fields need attention.", problem.GetProperty("title").GetString());

        using var arabic = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var arabicResponse = await arabic.PostAsJsonAsync("/api/identity/users", new { email = "x" });
        var arabicProblem = await Json(arabicResponse);
        Assert.Equal("بعض الحقول تحتاج إلى مراجعة.", arabicProblem.GetProperty("title").GetString());
        Assert.Equal("أدخل عنوان بريد إلكتروني صحيحًا.", arabicProblem.GetProperty("errors").GetProperty("email")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Duplicate_email_in_the_same_workspace_is_a_conflict()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var response = await admin.PostAsJsonAsync("/api/identity/users", new
        {
            email = Env.Email(Env.TenantA, "VIEWER").ToUpperInvariant(),
            displayName = "Duplicate",
            language = "en",
            password = ErpTestEnvironment.Password,
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("identity.emailTaken", (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Racing_deletes_and_unblocks_of_one_user_answer_without_a_server_error()
    {
        // p04 dbd6e84: a user removed between the target check and the load answered 500 (the G1
        // gate's parallel write phase hit it). Every request racing on one user, delete or unblock,
        // either succeeds or is told the user is gone (404) or changed under it (409), never a
        // server error. With deletes alone exactly one wins; once unblocks change the row, every
        // racing delete may lose (409), and a delete sent afterwards then removes the user.
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        for (var round = 0; round < 6; round++)
        {
            var withUnblocks = round % 2 == 1;
            var created = await admin.PostAsJsonAsync("/api/identity/users", new
            {
                email = $"race{round}.{Guid.NewGuid():N}@{Env.TenantA.EmailDomain}",
                displayName = $"Race {round}",
                language = "en",
                password = ErpTestEnvironment.Password,
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await Json(created)).GetProperty("id").GetString();
            var answers = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => withUnblocks && i % 3 == 2
                ? admin.PostAsync($"/api/identity/users/{id}/unblock", null)
                : admin.DeleteAsync($"/api/identity/users/{id}")));
            var seen = string.Join(", ", answers.Select(a => $"{a.RequestMessage!.Method} {(int)a.StatusCode}"));
            Assert.All(answers, a => Assert.True(a.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound or HttpStatusCode.Conflict, seen));
            var deleted = answers.Count(a => a.StatusCode == HttpStatusCode.NoContent && a.RequestMessage!.Method == HttpMethod.Delete);
            Assert.True(withUnblocks ? deleted <= 1 : deleted == 1, seen);
            if (deleted == 0)
            {
                Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/identity/users/{id}")).StatusCode);
            }
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/identity/users/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/identity/users/{id}")).StatusCode);
        }
    }

    [Fact]
    public async Task Saving_over_someone_elses_change_is_refused()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var created = await Json(await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Concurrent", nameAr = "متزامن", permissions = new[] { "identity.users.read" } }));
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetUInt32();
        var first = await admin.PutAsJsonAsync($"/api/identity/roles/{id}", new { nameEn = "Concurrent 1", nameAr = "متزامن", permissions = new[] { "identity.users.read" }, version });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await admin.PutAsJsonAsync($"/api/identity/roles/{id}", new { nameEn = "Concurrent 2", nameAr = "متزامن", permissions = new[] { "identity.users.read" }, version });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("concurrency", (await Json(second)).GetProperty("code").GetString());
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/identity/roles/{id}");
        Assert.Equal("Concurrent 1", current.GetProperty("nameEn").GetString());
    }

    [Fact]
    public async Task The_administrator_role_cannot_be_changed_or_deleted_and_grants_every_permission()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items");
        var system = roles.EnumerateArray().Single(r => r.GetProperty("isSystem").GetBoolean());
        var catalogue = await admin.GetFromJsonAsync<JsonElement>("/api/identity/permissions");
        Assert.Equal(
            catalogue.EnumerateArray().Select(p => p.GetProperty("key").GetString()).Order(),
            system.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Order());
        var delete = await admin.DeleteAsync($"/api/identity/roles/{system.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal("identity.systemRole", (await Json(delete)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Permission_catalogue_is_labelled_in_the_users_language()
    {
        using var arabic = await Env.SignInAsync(Env.Email(Env.TenantA, "admin.ar"));
        var catalogue = await arabic.GetFromJsonAsync<JsonElement>("/api/identity/permissions");
        var users = catalogue.EnumerateArray().Single(p => p.GetProperty("key").GetString() == "identity.users.read");
        Assert.Equal("عرض المستخدمين", users.GetProperty("label").GetString());
        Assert.Equal("المستخدمون والصلاحيات", users.GetProperty("moduleLabel").GetString());
    }

    [Fact]
    public async Task Changing_the_language_preference_follows_the_user()
    {
        var email = $"prefs@{Env.TenantA.EmailDomain}";
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items");
        var readOnly = roles.EnumerateArray().First(r => !r.GetProperty("isSystem").GetBoolean()
            && r.GetProperty("permissions").EnumerateArray().Any(p => p.GetString() == "identity.profile.update"));
        await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Prefs", language = "en", password = ErpTestEnvironment.Password, roleIds = new[] { readOnly.GetProperty("id").GetGuid() } });

        using var user = await Env.SignInAsync(email);
        var changed = await user.PutAsJsonAsync("/api/identity/me/preferences", new { language = "ar" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var again = await Env.SignInAsync(email);
        var session = await again.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("ar", session.GetProperty("user").GetProperty("language").GetString());
        var invalid = await again.PutAsJsonAsync("/api/identity/me/preferences", new { language = "fr" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task The_digits_preference_follows_the_user_and_leaves_the_language_alone()
    {
        var email = $"digits@{Env.TenantA.EmailDomain}";
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var roles = (await admin.GetFromJsonAsync<JsonElement>("/api/identity/roles")).GetProperty("items");
        var readOnly = roles.EnumerateArray().First(r => !r.GetProperty("isSystem").GetBoolean()
            && r.GetProperty("permissions").EnumerateArray().Any(p => p.GetString() == "identity.profile.update"));
        var created = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Digits", language = "ar", password = ErpTestEnvironment.Password, roleIds = new[] { readOnly.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var user = await Env.SignInAsync(email);
        var first = await user.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("latn", first.GetProperty("user").GetProperty("numerals").GetString());

        var changed = await user.PutAsJsonAsync("/api/identity/me/preferences", new { numerals = "arab" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var body = await Json(changed);
        Assert.Equal("arab", body.GetProperty("numerals").GetString());
        Assert.Equal("ar", body.GetProperty("language").GetString());

        using var again = await Env.SignInAsync(email);
        var session = await again.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("arab", session.GetProperty("user").GetProperty("numerals").GetString());
        Assert.Equal("ar", session.GetProperty("user").GetProperty("language").GetString());

        var both = await again.PutAsJsonAsync("/api/identity/me/preferences", new { language = "en", numerals = "latn" });
        Assert.Equal(HttpStatusCode.OK, both.StatusCode);
        var bothBody = await Json(both);
        Assert.Equal("en", bothBody.GetProperty("language").GetString());
        Assert.Equal("latn", bothBody.GetProperty("numerals").GetString());

        var invalid = await again.PutAsJsonAsync("/api/identity/me/preferences", new { numerals = "hanidec" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("oneOf", (await Json(invalid)).GetProperty("errors").GetProperty("numerals")[0].GetProperty("code").GetString());

        var empty = await again.PutAsJsonAsync("/api/identity/me/preferences", new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("nothingToChange", (await Json(empty)).GetProperty("errors").GetProperty("language")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task User_search_finds_by_name_or_email_and_escapes_wildcards()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var byName = await admin.GetFromJsonAsync<JsonElement>("/api/identity/users?search=omar%20haddad");
        Assert.Contains(byName.GetProperty("items").EnumerateArray(), u => u.GetProperty("email").GetString() == Env.Email(Env.TenantA, "viewer"));
        var wildcard = await admin.GetFromJsonAsync<JsonElement>("/api/identity/users?search=%25");
        Assert.Equal(0, wildcard.GetProperty("total").GetInt32());
        var paged = await admin.GetFromJsonAsync<JsonElement>("/api/identity/users?take=5000");
        Assert.True(paged.GetProperty("items").GetArrayLength() <= 200);
    }
}
