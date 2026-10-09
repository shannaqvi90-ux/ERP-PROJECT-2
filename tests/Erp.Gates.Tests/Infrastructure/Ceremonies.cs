using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Erp.Testing;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>
/// Writes a valid body alone cannot make: the body must answer something the product issued to the
/// caller first (adding a passkey answers creation options with a device's signed attestation). A
/// gate that needs such a write to succeed (the write oracle: tenant B first uses a value through
/// the very endpoint) asks here to complete the body, as the caller, before it sends it. A route
/// with no ceremony is left as it is.
/// </summary>
public static class Ceremonies
{
    /// <summary>Complete <paramref name="body"/> for <c>method path</c> as <paramref name="client"/>;
    /// false when the route has a ceremony that the product refused to start.</summary>
    public static async Task<bool> CompleteAsync(HttpClient client, string method, string path, JsonObject body)
    {
        if (method == "POST" && string.Equals(path.Split('?')[0].TrimEnd('/'), "/api/identity/me/passkeys", StringComparison.OrdinalIgnoreCase))
        {
            // A software authenticator answers the session's creation options (each call a new
            // device, so the passkeys never repeat a credential).
            using var options = await client.PostAsync("/api/identity/me/passkeys/options", null);
            if (!options.IsSuccessStatusCode || await options.Content.ReadFromJsonAsync<JsonObject>() is not { } issued)
            {
                return false;
            }
            using var device = new SoftwarePasskey();
            var answer = device.Registration(issued, body["name"]?.ToString() ?? "Gate device");
            foreach (var field in new[] { "clientDataJson", "attestationObject", "transports" })
            {
                body[field] = answer[field]!.DeepClone();
            }
        }
        return true;
    }
}
