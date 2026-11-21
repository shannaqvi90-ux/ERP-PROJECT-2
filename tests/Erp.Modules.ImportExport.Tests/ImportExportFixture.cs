using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Modules.Jobs.Engine;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.ImportExport.Tests;

public sealed class ImportExportFixture : ImportExportFixtureBase;

public abstract class ImportExportFixtureBase : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "erp-importexport-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Where attachment bytes are kept (a folder of the test's own unless a test says otherwise).</summary>
    protected virtual string StorageRoot => Root;

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?> { ["Erp:Attachments:Root"] = StorageRoot });

    public async ValueTask DisposeAsync()
    {
        await Env.DisposeAsync();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>What the jobs table says of jobs that did not succeed (for a failing test's message).</summary>
    public async Task<string> JobProblemsAsync()
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT type, status, error_code, error_en FROM jobs.jobs WHERE status NOT IN ('succeeded') ORDER BY created_at DESC LIMIT 5", admin);
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(string.Join(" | ", Enumerable.Range(0, 4).Select(i => reader.IsDBNull(i) ? "-" : reader.GetString(i))));
        }
        return string.Join("\n", lines) + "\nserver: " + string.Join("\n", Env.Factory.ErrorLog.Entries.TakeLast(3));
    }

    /// <summary>Run every job that is due, as the worker would.</summary>
    public async Task RunJobsAsync()
    {
        var runner = Env.Factory.Services.GetRequiredService<JobRunner>();
        for (var i = 0; i < 5; i++)
        {
            if (await runner.RunOnceAsync(100) == 0)
            {
                break;
            }
        }
    }
}

internal static class Api
{
    public static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public const string Contacts = "/api/importexport/contacts.contacts";

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(Cancel);
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode} {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<JsonElement> UploadAsync(HttpClient client, string name, byte[] bytes, string type = Contacts)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/octet-stream");
        content.Headers.Add("X-File-Name", Uri.EscapeDataString(name));
        using var response = await client.PostAsync($"{type}/imports", content, Cancel);
        return await ReadAsync(response, HttpStatusCode.Created);
    }

    public static Task<JsonElement> UploadCsvAsync(HttpClient client, string csv, string name = "contacts.csv") => UploadAsync(client, name, Encoding.UTF8.GetBytes(csv));

    public static async Task<JsonElement> GetSessionAsync(HttpClient client, Guid id) =>
        await client.GetFromJsonAsync<JsonElement>($"{Contacts}/imports/{id}", Cancel);

    public static async Task<JsonElement> RunAsync(ImportExportFixture fixture, HttpClient client, Guid id, bool dryRun, string onError = "reject", string onDuplicate = "create")
    {
        using var response = await client.PostAsJsonAsync($"{Contacts}/imports/{id}/run", new { dryRun, onError, onDuplicate }, Cancel);
        var answer = await ReadAsync(response, response.StatusCode == HttpStatusCode.Accepted ? HttpStatusCode.Accepted : HttpStatusCode.OK);
        // A small file is done when the answer comes; a large one is a background job.
        Assert.Equal(response.StatusCode == HttpStatusCode.Accepted, answer.GetProperty("status").GetString() == "running");
        await fixture.RunJobsAsync();
        var session = await GetSessionAsync(client, id);
        Assert.True(session.GetProperty("status").GetString() != "running", "the job did not finish: " + await fixture.JobProblemsAsync());
        return session;
    }

    public static async Task<string> UserHoldingAsync(ErpTestEnvironment env, HttpClient admin, string local, string[] permissions, string language = "en")
    {
        var email = $"{local}-{Guid.NewGuid().ToString("N")[..6]}@{env.TenantA.EmailDomain}";
        var role = await admin.PostAsJsonAsync("/api/identity/roles", new { nameEn = "Import " + local, nameAr = "استيراد " + local, permissions }, Cancel);
        Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        var roleId = (await role.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
        var user = await admin.PostAsJsonAsync("/api/identity/users", new { email, displayName = "Import " + local, language, password = ErpTestEnvironment.Password, roleIds = new[] { roleId } }, Cancel);
        Assert.Equal(HttpStatusCode.Created, user.StatusCode);
        return email;
    }

    public static string Unique(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..8];
}
