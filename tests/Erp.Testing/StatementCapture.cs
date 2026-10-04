using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Erp.Kernel.Data;
using Npgsql;

namespace Erp.Testing;

/// <summary>
/// What a statement sent to PostgreSQL said, beyond its text: the value of every parameter of a
/// statement that names a setting, and the backend process it ran on. Every pool the platform
/// builds is observed (<see cref="IDataSourceObserver"/>, installed in every test host); Npgsql
/// calls back only while someone listens to its activities (the isolation gate's
/// <c>SqlTrace</c>), so the other test projects pay nothing. The gate judges each tenant setting
/// by the value it actually sets, and treats a statement inside a request that carries no
/// capture as sent on a pool the platform did not build (blind to the gate).
/// </summary>
public static class StatementCapture
{
    /// <summary>Custom property of the Npgsql activity holding the <see cref="Captured"/> statements.</summary>
    public const string Property = "erp.test.statement";

    /// <summary>One SQL text and its parameter values (empty unless the text names a setting).</summary>
    public sealed record Command(string Text, IReadOnlyDictionary<string, string?> Parameters);

    /// <summary>The statements of one Npgsql command or batch, its backend process, and for a
    /// statement that changes a setting the product code that sent it.</summary>
    /// <param name="Caller">Innermost product type on the stack (outside this test library) when a
    /// statement that changes a session setting was sent; null for every other statement.</param>
    public sealed record Captured(IReadOnlyList<Command> Commands, int ProcessId, string? Caller = null);

    /// <summary>A statement that changes a session or transaction setting.</summary>
    private static readonly Regex SettingStatement = new(
        @"\bset_config\s*\(|(^|;)\s*(set|reset|discard)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True when the statement changes a session or transaction setting
    /// (<c>set_config</c>, <c>SET</c>, <c>RESET</c>, <c>DISCARD</c>).</summary>
    public static bool ChangesSettings(string text) => SettingStatement.IsMatch(text);

    /// <summary>True when <paramref name="text"/> may change or name a session setting, so its
    /// parameter values are kept.</summary>
    public static bool NamesSettings(string text) =>
        ChangesSettings(text) || text.Contains("app.", StringComparison.OrdinalIgnoreCase);

    private static Command Of(string text, NpgsqlParameterCollection parameters) =>
        new(text, NamesSettings(text) ? Values(parameters) : Empty);

    /// <summary>The product code that sent a statement: the innermost type of an <c>Erp.</c>
    /// namespace outside this test library (the class whose method, async state machine, lambda
    /// or local function sent it). Only worked out for statements that change settings: a stack
    /// walk per statement is the costliest part of watching a request.</summary>
    private static string? CallerOf(IEnumerable<string> texts)
    {
        if (!texts.Any(ChangesSettings))
        {
            return null;
        }
        foreach (var frame in new StackTrace(2, false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            while (type?.DeclaringType is not null)
            {
                type = type.DeclaringType;
            }
            if (type?.Namespace is { } ns && ns.StartsWith("Erp.", StringComparison.Ordinal) && !ns.StartsWith("Erp.Testing", StringComparison.Ordinal))
            {
                return type.FullName;
            }
        }
        return null;
    }

    private static readonly IReadOnlyDictionary<string, string?> Empty = new Dictionary<string, string?>();

    private static Dictionary<string, string?> Values(NpgsqlParameterCollection parameters)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var value = parameter.Value is null or DBNull ? null : Convert.ToString(parameter.Value, CultureInfo.InvariantCulture);
            // Named (@tenant) and positional ($1) references.
            if (!string.IsNullOrEmpty(parameter.ParameterName))
            {
                values[parameter.ParameterName.TrimStart('@', ':')] = value;
            }
            values["$" + (i + 1).ToString(CultureInfo.InvariantCulture)] = value;
        }
        return values;
    }

    /// <summary>Installs the capture on every pool the platform builds.</summary>
    internal sealed class Observer : IDataSourceObserver
    {
        public void Configure(NpgsqlDataSourceBuilder builder) => builder.ConfigureTracing(tracing => tracing
            .ConfigureCommandEnrichmentCallback((activity, command) =>
                activity.SetCustomProperty(Property, new Captured([Of(command.CommandText, command.Parameters)], ProcessOf(command), CallerOf([command.CommandText]))))
            .ConfigureBatchEnrichmentCallback((activity, batch) =>
            {
                var commands = Enumerable.Select<NpgsqlBatchCommand, Command>(batch.BatchCommands, c => Of(c.CommandText, c.Parameters)).ToList();
                activity.SetCustomProperty(Property, new Captured(commands, ProcessOf(batch), CallerOf(commands.Select(c => c.Text))));
            })
            .ConfigureCopyOperationEnrichmentCallback((activity, command) =>
                activity.SetCustomProperty(Property, new Captured([new Command(command, Empty)], 0))));

        // Commands created from a data source (dataSource.CreateCommand) refuse access to their
        // connection; their backend is then unknown (0).
        private static int ProcessOf(NpgsqlCommand command)
        {
            try
            {
                return command.Connection?.ProcessID ?? 0;
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
            {
                return 0;
            }
        }

        private static int ProcessOf(NpgsqlBatch batch)
        {
            try
            {
                return batch.Connection?.ProcessID ?? 0;
            }
            catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
            {
                return 0;
            }
        }
    }

    /// <summary>The capture an activity carries, if any.</summary>
    public static Captured? Of(Activity activity) => activity.GetCustomProperty(Property) as Captured;
}
