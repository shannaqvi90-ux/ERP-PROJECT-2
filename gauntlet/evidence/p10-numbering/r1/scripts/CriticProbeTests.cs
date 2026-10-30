using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Modules.Numbering.Contracts;
using Erp.Testing;

namespace Erp.Modules.Numbering.Tests;

// Critic p10 r1 probe: a sequence switched from a yearly run to "never" and back within the same year.
public sealed class CriticProbeTests(NumberingFixture fixture, ITestOutputHelper output) : IClassFixture<NumberingFixture>
{
    private ErpTestEnvironment Env => fixture.Env;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(4));
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> Put(HttpClient c, JsonElement rec, string prefix, string reset, bool gapless, long? next = null)
    {
        var r = await c.PutAsJsonAsync($"/api/numbering/sequences/{rec.GetProperty("id").GetGuid()}", new
        {
            prefix, suffix = rec.GetProperty("suffix").GetString(), padding = 5, reset, gapless, perBranch = false, nextValue = next,
            version = rec.GetProperty("version").GetUInt32(),
        });
        var body = await r.Content.ReadAsStringAsync();
        output.WriteLine($"PUT prefix={prefix} reset={reset} gapless={gapless} next={next} -> {(int)r.StatusCode} {body[..Math.Min(400, body.Length)]}");
        Assert.True(r.StatusCode == HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<string> Take(Guid company, string type)
    {
        await using var unit = await Unit.JobAsync(Env, Env.TenantA.Id);
        try
        {
            var n = await unit.TakeAsync(type, company, Today);
            await unit.CommitAsync();
            output.WriteLine($"take -> {n.Number}");
            return n.Number;
        }
        catch (NumberingException e)
        {
            output.WriteLine($"take refused -> {e.Code} {string.Join(",", e.Arguments)}");
            await unit.RollbackAsync();
            return "REFUSED:" + e.Code;
        }
    }

    [Theory]
    [InlineData("sales.order", false)]
    [InlineData("sales.quotation", true)]
    public async Task Yearly_never_yearly_toggle_in_the_same_year(string type, bool gapless)
    {
        using var admin = await Env.SignInAsync(Env.Email(Env.TenantA, "admin"));
        var companies = await Db.CompaniesAsync(Env, Env.TenantA.Id);
        var rows = (await Json(await admin.GetAsync("/api/numbering/sequences?take=200"))).GetProperty("items").EnumerateArray().ToList();
        output.WriteLine("types: " + string.Join(",", rows.Select(r => r.GetProperty("documentType").GetString()).Distinct()));
        var row = rows.First(r => r.GetProperty("documentType").GetString() == type && r.GetProperty("companyId").GetGuid() == companies[0]);
        var rec = await Put(admin, row, "SX-{YYYY}{MM}-", "yearly", gapless);
        for (var i = 0; i < 3; i++) await Take(companies[0], type);
        rec = await Put(admin, rec, "SX-{YYYY}{MM}-", "never", gapless);
        for (var i = 0; i < 2; i++) await Take(companies[0], type);
        rec = await Put(admin, rec, "SX-{YYYY}{MM}-", "yearly", gapless);
        output.WriteLine($"preview after switching back: {rec.GetProperty("nextNumber").GetString()}");
        var results = new List<string>();
        for (var i = 0; i < 3; i++) results.Add(await Take(companies[0], type));
        // Can the administrator recover by setting the next number?
        var fix = await admin.PutAsJsonAsync($"/api/numbering/sequences/{rec.GetProperty("id").GetGuid()}", new
        {
            prefix = "SX-{YYYY}{MM}-", suffix = rec.GetProperty("suffix").GetString(), padding = 5, reset = "yearly", gapless, perBranch = false, nextValue = 50L,
            version = rec.GetProperty("version").GetUInt32(),
        });
        output.WriteLine($"set next number 50 -> {(int)fix.StatusCode} {(await fix.Content.ReadAsStringAsync())[..Math.Min(300, (await fix.Content.ReadAsStringAsync()).Length)]}");
        Assert.DoesNotContain(results, r => r.StartsWith("REFUSED", StringComparison.Ordinal));
    }
}
