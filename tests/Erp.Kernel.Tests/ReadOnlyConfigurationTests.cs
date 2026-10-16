using Erp.Kernel.Hosting;
using Microsoft.Extensions.Configuration;

namespace Erp.Kernel.Tests;

/// <summary>The configuration takes writes while the app is configured and refuses them once it is
/// sealed, before any provider takes the value; reads are untouched.</summary>
public sealed class ReadOnlyConfigurationTests
{
    private static ConfigurationManager Configured()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Erp:Name"] = "erp", ["Erp:Nested:Value"] = "1" });
        ReadOnlyConfiguration.Install(configuration);
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Erp:Later"] = "later" });
        return configuration;
    }

    [Fact]
    public void Writes_work_until_sealed_and_fail_after_without_reaching_any_provider()
    {
        var configuration = Configured();
        configuration["Erp:Before"] = "before";
        Assert.Equal("before", configuration["Erp:Before"]);
        Assert.False(ReadOnlyConfiguration.IsSealed(configuration));

        ReadOnlyConfiguration.Seal(configuration);

        Assert.True(ReadOnlyConfiguration.IsSealed(configuration));
        var refused = Assert.Throws<InvalidOperationException>(() => configuration["Erp:Count"] = "1");
        Assert.Contains("Erp:Count", refused.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => configuration.GetSection("Erp:Nested")["Value"] = "2");
        Assert.Throws<InvalidOperationException>(() => configuration["Erp:Name"] = "changed");
        Assert.Null(configuration["Erp:Count"]);
        Assert.Equal("1", configuration["Erp:Nested:Value"]);
        Assert.Equal("erp", configuration["Erp:Name"]);
        Assert.Equal("later", configuration["Erp:Later"]);
        Assert.Equal("before", configuration["Erp:Before"]);
    }

    [Fact]
    public void The_guard_adds_no_keys_and_is_installed_once()
    {
        var configuration = Configured();
        ReadOnlyConfiguration.Install(configuration);
        var plain = new ConfigurationManager();
        plain.AddInMemoryCollection(new Dictionary<string, string?> { ["Erp:Name"] = "erp", ["Erp:Nested:Value"] = "1", ["Erp:Later"] = "later" });
        Assert.Equal(plain.AsEnumerable().OrderBy(p => p.Key, StringComparer.Ordinal), configuration.AsEnumerable().OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Single(((IConfigurationBuilder)configuration).Sources, s => s.GetType().Name == "GuardSource");
        Assert.Equal(["Erp"], configuration.GetChildren().Select(c => c.Key));
    }
}
