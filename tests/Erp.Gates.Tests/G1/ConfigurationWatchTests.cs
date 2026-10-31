using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>The non-interference check compares each host's configuration before and after every
/// request it judges. The comparison looks at the providers' data in place; these tests prove it
/// reports every kind of write the full read reported (a key set, changed or removed, in any
/// provider, and through a chained configuration) and nothing when nothing was written.</summary>
public sealed class ConfigurationWatchTests
{
    private static (IServiceProvider Services, ConfigurationManager Configuration) Host(Action<ConfigurationManager>? shape = null)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["a:b"] = "1", ["c"] = "2" });
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["d"] = "3" });
        shape?.Invoke(configuration);
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();
        return (services, configuration);
    }

    private static List<string> Check(NonInterference.ConfigurationWatch watch)
    {
        var state = new NonInterference.State();
        watch.Check(state, "a request");
        return state.Findings;
    }

    [Fact]
    public void Nothing_written_is_nothing_reported_over_many_checks()
    {
        var (services, _) = Host();
        var watch = new NonInterference.ConfigurationWatch();
        watch.Watch("host", services);
        for (var i = 0; i < 50; i++)
        {
            Assert.Empty(Check(watch));
        }
    }

    [Fact]
    public void A_key_set_changed_or_removed_in_a_provider_is_reported_once_each()
    {
        var (services, configuration) = Host();
        var watch = new NonInterference.ConfigurationWatch();
        watch.Watch("host", services);

        configuration["fresh"] = "x";
        var added = Check(watch);
        Assert.Single(added);
        Assert.Contains("fresh: unset", added[0], StringComparison.Ordinal);
        Assert.Empty(Check(watch));

        configuration["c"] = "other";
        var changed = Check(watch);
        Assert.Single(changed);
        Assert.Contains("c: '2'", changed[0], StringComparison.Ordinal);
        Assert.Empty(Check(watch));

        configuration["c"] = null;
        var removed = Check(watch);
        Assert.Single(removed);
        Assert.Empty(Check(watch));
    }

    [Fact]
    public void A_write_through_a_chained_configuration_is_reported_too()
    {
        var inner = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["inner"] = "1" }).Build();
        var (services, configuration) = Host(c => c.AddConfiguration(inner));
        var watch = new NonInterference.ConfigurationWatch();
        watch.Watch("host", services);
        Assert.Empty(Check(watch));
        configuration["inner"] = "2";
        Assert.NotEmpty(Check(watch));
        Assert.Empty(Check(watch));
    }
}
