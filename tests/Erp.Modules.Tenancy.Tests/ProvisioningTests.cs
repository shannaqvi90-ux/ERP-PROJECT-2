using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Kernel.Modules;
using Erp.Modules.Tenancy.Provisioning;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Tenancy.Tests;

/// <summary>Platform operators provision, suspend and reactivate workspaces from the command line
/// (<c>Erp.Host tenant …</c>); workspace settings are kept per workspace.</summary>
[Collection(ConsoleCollection.Name)]
public sealed class ProvisioningTests(TenancyFixture fixture) : IClassFixture<TenancyFixture>
{
    private ErpTestEnvironment Env => fixture.Env;

    private async Task<(int Code, string Output)> RunAsync(params string[] args)
    {
        var command = Env.Factory.Services.GetRequiredService<ModuleCatalog>().Modules.SelectMany(m => m.Commands).Single(c => c.Verb == "tenant");
        var output = new StringWriter();
        var error = new StringWriter();
        var (previousOut, previousError) = (Console.Out, Console.Error);
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var code = await command.Run(Env.Factory.Services, args, CancellationToken.None);
            return (code, output + error.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    /// <summary>The audit trail's correlations of one record's rows, oldest first.</summary>
    private async Task<List<string>> CorrelationsAsync(string table, Guid id)
    {
        await using var db = await Env.OpenAdminAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT coalesce(correlation_id, '') FROM audit.entries WHERE table_name = @table AND record_id = @id ORDER BY id", db);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("id", id);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }
        return rows;
    }

    [Fact]
    public async Task An_operator_provisions_a_workspace_with_its_first_company_branch_and_administrator()
    {
        Environment.SetEnvironmentVariable(TenantCommand.PasswordVariable, "Acme-Initial-Pass-1");
        try
        {
            var (code, output) = await RunAsync("create", "--code", "acme-trading", "--name-en", "Acme Trading LLC", "--name-ar", "أكمي للتجارة ذ.م.م",
                "--admin-email", "owner@acme.example", "--admin-name", "Sara Rahman", "--language", "ar");
            Assert.True(code == 0, output);
            Assert.Contains("acme-trading", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Acme-Initial-Pass-1", output, StringComparison.Ordinal);

            using var owner = await Env.SignInAsync("owner@acme.example", "Acme-Initial-Pass-1");
            var session = await owner.GetFromJsonAsync<JsonElement>("/api/auth/session");
            Assert.Equal("acme-trading", session.GetProperty("tenant").GetProperty("code").GetString());
            Assert.Equal("ar", session.GetProperty("user").GetProperty("language").GetString());
            Assert.Contains("tenancy.companies.create", session.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
            var workplace = await owner.GetFromJsonAsync<JsonElement>("/api/tenancy/workplace");
            var company = workplace.GetProperty("companies").EnumerateArray().Single();
            Assert.Equal("ACME-TRADING", company.GetProperty("code").GetString());
            Assert.Equal("أكمي للتجارة ذ.م.م", company.GetProperty("legalNameAr").GetString());
            Assert.Equal("HQ", company.GetProperty("branches").EnumerateArray().Single().GetProperty("code").GetString());
            var tenant = await owner.GetFromJsonAsync<JsonElement>("/api/tenancy/tenant");
            Assert.Equal("ar", tenant.GetProperty("defaultLanguage").GetString());
            Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>("/api/identity/users")).GetProperty("total").GetInt32());

            var (again, message) = await RunAsync("create", "--code", "acme-trading", "--name-en", "Other", "--name-ar", "أخرى",
                "--admin-email", "x@acme.example", "--admin-name", "X");
            Assert.Equal(2, again);
            Assert.Contains("already exists", message, StringComparison.Ordinal);
            // A plain answer, no database error or stack trace before it (critic p02 round 4).
            Assert.Equal("tenant create: a workspace with code 'acme-trading' already exists.", message.Trim());

            // The audit trail names the operator who provisioned the workspace (by default the
            // account and machine the command ran on).
            var created = await CorrelationsAsync("tenants", session.GetProperty("tenant").GetProperty("id").GetGuid());
            Assert.NotEmpty(created);
            Assert.All(created, c => Assert.Equal($"operator:{Environment.UserName}@{Environment.MachineName}:tenant-create", c));

            var (listed, list) = await RunAsync("list");
            Assert.Equal(0, listed);
            Assert.Contains("acme-trading\tactive\t1\tAcme Trading LLC", list, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TenantCommand.PasswordVariable, null);
        }
    }

    [Fact]
    public async Task Suspending_a_workspace_ends_its_sessions_and_activating_it_restores_sign_in()
    {
        using var viewer = await Env.SignInAsync(Env.Email(Env.TenantB, "viewer"));
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/tenancy/tenant")).StatusCode);

        var before = (await CorrelationsAsync("tenants", Env.TenantB.Id)).Count;
        var (suspended, output) = await RunAsync("suspend", "--code", Env.TenantB.Code, "--operator", "ops.sara@platform");
        Assert.True(suspended == 0, output);
        // Who suspended the workspace is in its audit trail (critic p02 round 4: actor 'system' only).
        Assert.Equal("operator:ops.sara@platform:tenant-suspend", (await CorrelationsAsync("tenants", Env.TenantB.Id)).Skip(before).Single());
        Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/tenancy/tenant")).StatusCode);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Env.SignInAsync(Env.Email(Env.TenantB, "admin")));
        // The other workspace is untouched.
        using (var other = await Env.SignInAsync(Env.Email(Env.TenantA, "viewer")))
        {
            Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/tenancy/tenant")).StatusCode);
        }

        Environment.SetEnvironmentVariable(TenantCommand.OperatorVariable, "ops.omar");
        int activated;
        try
        {
            (activated, _) = await RunAsync("activate", "--code", Env.TenantB.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TenantCommand.OperatorVariable, null);
        }
        Assert.Equal(0, activated);
        Assert.Equal("operator:ops.omar:tenant-activate", (await CorrelationsAsync("tenants", Env.TenantB.Id)).Last());
        var (refused, why) = await RunAsync("suspend", "--code", Env.TenantB.Code, "--operator", "two words");
        Assert.Equal(2, refused);
        Assert.Contains("--operator", why, StringComparison.Ordinal);
        using var back = await Env.SignInAsync(Env.Email(Env.TenantB, "admin"));
        Assert.Equal("active", (await back.GetFromJsonAsync<JsonElement>("/api/tenancy/tenant")).GetProperty("status").GetString());
        Assert.Equal(2, (await RunAsync("suspend", "--code", "no-such-workspace")).Code);
        Assert.Equal(2, (await RunAsync("create", "--code", "Bad Code!")).Code);
    }

    [Fact]
    public async Task Workspace_settings_are_validated_and_kept()
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var tenant = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/tenant");
        Assert.Equal("Asia/Dubai", tenant.GetProperty("timeZone").GetString());
        Assert.Equal("monday", tenant.GetProperty("weekStart").GetString());
        Assert.Contains("Asia/Riyadh", tenant.GetProperty("timeZones").EnumerateArray().Select(z => z.GetString()));

        var bad = await admin.PutAsJsonAsync("/api/tenancy/tenant", new { nameEn = "Alpha", nameAr = "ألفا", timeZone = "Mars/Olympus", version = tenant.GetProperty("version").GetUInt32() });
        Assert.Equal("tenancyTimeZone", (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").GetProperty("timeZone")[0].GetProperty("code").GetString());

        var saved = await admin.PutAsJsonAsync("/api/tenancy/tenant", new
        {
            nameEn = tenant.GetProperty("nameEn").GetString(),
            nameAr = tenant.GetProperty("nameAr").GetString(),
            defaultLanguage = "ar",
            timeZone = "Asia/Riyadh",
            weekStart = "sunday",
            version = tenant.GetProperty("version").GetUInt32(),
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var after = await admin.GetFromJsonAsync<JsonElement>("/api/tenancy/tenant");
        Assert.Equal("ar", after.GetProperty("defaultLanguage").GetString());
        Assert.Equal("Asia/Riyadh", after.GetProperty("timeZone").GetString());
        Assert.Equal("sunday", after.GetProperty("weekStart").GetString());
    }
}

/// <summary>Tests that redirect the console run one at a time.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCollection
{
    public const string Name = "console";
}
