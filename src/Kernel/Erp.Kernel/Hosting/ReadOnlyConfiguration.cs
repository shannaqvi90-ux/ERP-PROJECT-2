using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Erp.Kernel.Hosting;

/// <summary>
/// The app's configuration becomes read-only once the app is built. Every tenant shares one
/// process and one <see cref="IConfiguration"/>, so a value written into it while serving a request
/// (a counter, a "last caller" note) is process-wide state that one tenant can read back from
/// another's activity (critic p04 round 8, plant L3: a count of every tenant's Arabic switches kept
/// in configuration and sent in a response header). Configuration is read at start-up from files,
/// the environment and the command line; nothing has a reason to write it afterwards.
/// <para>
/// A write through the indexer (<c>configuration[key] = value</c>, also through a section) goes to
/// every provider in order; <see cref="Install"/> puts a provider first whose write throws once
/// <see cref="Seal"/> has run, so the write fails before any other provider takes the value. Reads
/// are unaffected: this provider holds no keys. A write straight into another provider still gets
/// past it; the G1 non-interference gate compares the configuration before and after every
/// request it judges and reports any change.
/// </para>
/// </summary>
public static class ReadOnlyConfiguration
{
    /// <summary>Put the guard first among <paramref name="builder"/>'s sources (while the app is
    /// being configured); writes still work until <see cref="Seal"/>.</summary>
    public static void Install(IConfigurationBuilder builder)
    {
        if (builder.Sources.OfType<GuardSource>().Any())
        {
            return;
        }
        builder.Sources.Insert(0, new GuardSource());
    }

    /// <summary>From now on every write through <paramref name="configuration"/> throws.</summary>
    public static void Seal(IConfiguration configuration)
    {
        if (configuration is IConfigurationBuilder builder)
        {
            foreach (var source in builder.Sources.OfType<GuardSource>())
            {
                source.Seal();
            }
        }
    }

    /// <summary>Whether writes through <paramref name="configuration"/> are refused.</summary>
    public static bool IsSealed(IConfiguration configuration) =>
        configuration is IConfigurationBuilder builder && builder.Sources.OfType<GuardSource>().Any(s => s.IsSealed);

    /// <summary>The guard. Its one field is a latch that goes from open to sealed once, as the app
    /// is built; it holds no data of any request or tenant.</summary>
    private sealed class GuardSource : IConfigurationSource
    {
        private int _sealed;

        public bool IsSealed => Volatile.Read(ref _sealed) == 1;

        public void Seal() => Interlocked.Exchange(ref _sealed, 1);

        public IConfigurationProvider Build(IConfigurationBuilder builder) => new GuardProvider(this);
    }

    private sealed class GuardProvider(GuardSource source) : IConfigurationProvider
    {
        public bool TryGet(string key, out string? value)
        {
            value = null;
            return false;
        }

        public void Set(string key, string? value)
        {
            if (source.IsSealed)
            {
                throw new InvalidOperationException(
                    $"The configuration is read-only once the app is built: it is shared by every tenant of the process, so a value written into it while serving a request (key '{key}') would reach other tenants. Keep request state in the request, and tenant state in the tenant's own rows.");
            }
        }

        public IChangeToken GetReloadToken() => NeverChanges.Instance;

        public void Load()
        {
        }

        public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath) => earlierKeys;
    }

    private sealed class NeverChanges : IChangeToken
    {
        public static readonly NeverChanges Instance = new();

        public bool HasChanged => false;

        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
