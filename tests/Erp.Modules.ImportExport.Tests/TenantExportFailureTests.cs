using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Testing;

namespace Erp.Modules.ImportExport.Tests;

/// <summary>A workspace whose attachment storage cannot be reached, as the worker of round 1's stack could not (it had no volume).</summary>
public sealed class BrokenStorageFixture : ImportExportFixtureBase
{
    // The folder becomes a file, so the attachment store cannot make its folder: it fails the way it did in the worker without the volume.
    protected override void AfterStart()
    {
        Directory.Delete(Root, recursive: true);
        File.WriteAllText(Root, "not a folder");
    }
}

/// <summary>What the person sees when a full export cannot be written, and what they can do next.</summary>
public sealed class TenantExportFailureTests(BrokenStorageFixture fixture) : IClassFixture<BrokenStorageFixture>
{
    private static CancellationToken Cancel => Api.Cancel;
    private const string Root = "/api/importexport/tenant-exports";

    [Fact]
    public async Task An_export_that_cannot_be_written_ends_as_failed_with_its_reason_and_a_new_one_can_be_started()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantA, "admin"));
        using var start = await admin.PostAsJsonAsync(Root, new { format = "csv" }, Cancel);
        var first = (await Api.ReadAsync(start, HttpStatusCode.Accepted)).GetProperty("id").GetGuid();
        await fixture.RunJobsAsync();

        var failed = await admin.GetFromJsonAsync<JsonElement>($"{Root}/{first}", Cancel);
        Assert.True(failed.GetProperty("status").GetString() == "failed", failed.ToString() + await fixture.JobProblemsAsync());
        Assert.Equal("exportStorage", failed.GetProperty("failureCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(failed.GetProperty("failure").GetString()));
        Assert.False(failed.GetProperty("downloadable").GetBoolean());

        // The reason is in the caller's language.
        using var arabicUser = await fixture.Env.SignInAsync(await Api.UserHoldingAsync(fixture.Env, admin, "arabic", ["importexport.exports.read"], "ar"));
        var inArabic = await arabicUser.GetFromJsonAsync<JsonElement>($"{Root}/{first}", Cancel);
        Assert.Matches("[؀-ۿ]", inArabic.GetProperty("failure").GetString()!);

        // A failed export does not block the next one.
        using var again = await admin.PostAsJsonAsync(Root, new { format = "json" }, Cancel);
        var next = await Api.ReadAsync(again, HttpStatusCode.Accepted);
        Assert.NotEqual(first, next.GetProperty("id").GetGuid());
        Assert.Equal("queued", next.GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_export_that_never_finished_is_given_up_after_hours_and_does_not_block_a_new_one()
    {
        using var admin = await fixture.Env.SignInAsync(fixture.Env.Email(fixture.Env.TenantB, "admin"));
        using var start = await admin.PostAsJsonAsync(Root, new { format = "csv" }, Cancel);
        var stuck = (await Api.ReadAsync(start, HttpStatusCode.Accepted)).GetProperty("id").GetGuid();
        await using (var db = await fixture.Env.OpenAdminAsync())
        {
            await using var age = new Npgsql.NpgsqlCommand("UPDATE importexport.tenant_exports SET created_at = now() - interval '6 hours' WHERE id = @id", db);
            age.Parameters.AddWithValue("id", stuck);
            await age.ExecuteNonQueryAsync(Cancel);
        }
        var seen = await admin.GetFromJsonAsync<JsonElement>($"{Root}/{stuck}", Cancel);
        Assert.Equal("failed", seen.GetProperty("status").GetString());
        Assert.Equal("exportStalled", seen.GetProperty("failureCode").GetString());
        using var again = await admin.PostAsJsonAsync(Root, new { format = "csv" }, Cancel);
        Assert.NotEqual(stuck, (await Api.ReadAsync(again, HttpStatusCode.Accepted)).GetProperty("id").GetGuid());
    }
}
