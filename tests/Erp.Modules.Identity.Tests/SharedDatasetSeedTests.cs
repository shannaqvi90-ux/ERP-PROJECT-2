using Erp.Kernel.Seeding;
using Erp.Testing;
using Npgsql;

namespace Erp.Modules.Identity.Tests;

/// <summary>
/// The comparison harness loads the same users into both products: with Erp:Seed:UsersCsv set,
/// the main demo tenant's bulk users are exactly the shared dataset's users (names, sign-ins and
/// languages as the Odoo reference loads them); the other tenants keep generated users.
/// </summary>
public sealed class SharedDatasetSeedTests
{
    [Fact]
    public async Task Demo_seed_loads_the_shared_dataset_users_into_the_main_tenant()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"users-{Guid.NewGuid():N}.csv");
        await File.WriteAllLinesAsync(csv,
        [
            "ref,name,name_ar,login,lang",
            "U000001,Mariam Hassan Khoury,مريم حسن خوري,mariam.khoury.000001@staff.example,en",
            "U000002,\"Noura \"\"Nono\"\" Al Ketbi\",نورة الكتبي,noura.al.ketbi.000002@staff.example,ar",
            "U000003,Rahul Menon,راهول مينون,rahul.menon.000003@staff.example,en",
            "U000004,Not Loaded,لا,not.loaded.000004@staff.example,en",
        ]);
        try
        {
            await using var env = await ErpTestEnvironment.StartAsync(SeedPlan.Demo(3, ErpTestEnvironment.Password),
                new Dictionary<string, string?> { ["Erp:Seed:UsersCsv"] = csv });
            await using var admin = await env.OpenAdminAsync();
            async Task<List<(string Email, string Name, string Language)>> UsersOf(Guid tenant, string like)
            {
                await using var command = new NpgsqlCommand(
                    "SELECT email, display_name, language FROM identity.users WHERE tenant_id = @t AND email LIKE @l ORDER BY email", admin);
                command.Parameters.AddWithValue("t", tenant);
                command.Parameters.AddWithValue("l", like);
                await using var reader = await command.ExecuteReaderAsync();
                var rows = new List<(string, string, string)>();
                while (await reader.ReadAsync())
                {
                    rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                }
                return rows;
            }

            var main = await UsersOf(env.Plan.Tenants[0].Id, "%@staff.example");
            Assert.Equal(
            [
                ("mariam.khoury.000001@staff.example", "Mariam Hassan Khoury", "en"),
                ("noura.al.ketbi.000002@staff.example", "Noura \"Nono\" Al Ketbi", "ar"),
                ("rahul.menon.000003@staff.example", "Rahul Menon", "en"),
            ], main);
            Assert.Empty(await UsersOf(env.Plan.Tenants[1].Id, "%@staff.example"));
            Assert.NotEmpty(await UsersOf(env.Plan.Tenants[1].Id, $"%.%@{env.Plan.Tenants[1].EmailDomain}"));

            // A dataset user signs in with the demo password.
            using var client = await env.SignInAsync("rahul.menon.000003@staff.example");
        }
        finally
        {
            File.Delete(csv);
        }
    }
}
