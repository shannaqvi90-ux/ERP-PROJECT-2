using Erp.Modules.Jobs.Contracts;

namespace Erp.Modules.Jobs.Engine;

/// <summary>The registered kinds of job. Refuses two registrations of the same key at start-up.</summary>
public sealed class JobTypes : IJobTypes
{
    private readonly Dictionary<string, JobTypeRegistration> _byKey;

    public JobTypes(IEnumerable<JobTypeRegistration> registrations)
    {
        var list = registrations.ToList();
        var duplicates = list.GroupBy(r => r.Type.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException($"Job types registered twice: {string.Join(", ", duplicates)}");
        }
        _byKey = list.ToDictionary(r => r.Type.Key, StringComparer.Ordinal);
        All = [.. list.Select(r => r.Type).OrderBy(t => t.Key, StringComparer.Ordinal)];
    }

    public IReadOnlyList<JobType> All { get; }

    public JobType? Find(string key) => _byKey.TryGetValue(key, out var found) ? found.Type : null;

    /// <summary>The handler type of a kind, or null.</summary>
    public Type? HandlerOf(string key) => _byKey.TryGetValue(key, out var found) ? found.Handler : null;
}
