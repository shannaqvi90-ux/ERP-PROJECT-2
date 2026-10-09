using System.Runtime.CompilerServices;
using Erp.Testing;

namespace Erp.Gates.Tests.Infrastructure;

/// <summary>Settings every gate environment of this project starts with (under each test's own).</summary>
internal static class GateSettings
{
    [ModuleInitializer]
    internal static void Apply() => ErpTestEnvironment.ProjectGateSettings = new Dictionary<string, string?>
    {
        // A session may add a passkey for an hour after signing in (the product's longest): tenant
        // B's activity signs in once and adds passkeys through the whole attack (G1, Ceremonies).
        ["Erp:Auth:PasskeyRecentSignInMinutes"] = "60",
    };
}
