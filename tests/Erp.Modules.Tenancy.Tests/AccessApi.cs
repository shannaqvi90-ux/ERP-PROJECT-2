using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Modules.Tenancy.Tests;

/// <summary>Saving a user's company access the way the access screen does: read it, then send the
/// change with the version that was read.</summary>
internal static class AccessApi
{
    public static async Task<uint> AccessVersionAsync(this HttpClient client, Guid userId)
    {
        using var response = await client.GetAsync($"/api/tenancy/access/{userId}");
        return response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32() : 0;
    }

    /// <summary>PUT the body with the version the same client reads first (unless the body carries one).</summary>
    public static async Task<HttpResponseMessage> PutAccessAsync(this HttpClient client, Guid userId, object body)
    {
        var json = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        if (!json.ContainsKey("version"))
        {
            json["version"] = await client.AccessVersionAsync(userId);
        }
        return await client.PutAsJsonAsync($"/api/tenancy/access/{userId}", json);
    }
}
