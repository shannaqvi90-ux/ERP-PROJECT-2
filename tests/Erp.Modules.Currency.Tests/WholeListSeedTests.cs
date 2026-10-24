using Erp.Kernel.Money;
using Erp.Kernel.Seeding;
using Erp.Testing;

namespace Erp.Modules.Currency.Tests;

/// <summary>
/// Outside the gate workspaces (which hold a sample, <c>CurrencySeeder.GateSample</c>) every
/// workspace holds every current ISO 4217 currency, with ISO's English name and the product's Arabic
/// name, the dirham in use: here a workspace seeded with the minimal profile, the one a new
/// workspace's seed run shares its currency rules with.
/// </summary>
public sealed class WholeListSeedTests
{
    [Fact]
    public async Task A_workspace_outside_the_gates_holds_every_ISO_4217_currency_with_both_names_and_the_dirham_in_use()
    {
        await using var env = await ErpTestEnvironment.StartAsync(SeedPlan.Minimal(ErpTestEnvironment.Password));
        await using var admin = await env.OpenAdminAsync();
        await using var command = admin.CreateCommand();
        command.CommandText = "SELECT code, name_en, name_ar, is_active FROM currency.currencies ORDER BY code";
        var rows = new List<(string Code, string NameEn, string NameAr, bool IsActive)>();
        await using (var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
            }
        }
        Assert.Equal(CurrencyCode.All.Order(StringComparer.Ordinal), rows.Select(r => r.Code));
        Assert.Equal(165, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.NameEn) || string.IsNullOrWhiteSpace(r.NameAr), r.Code));
        Assert.Contains(rows, r => r.Code == "AED" && r.IsActive && r.NameEn == "UAE Dirham");
    }
}
