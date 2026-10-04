using System.Diagnostics;
using System.Globalization;
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

    /// <summary>The statements of one Npgsql command or batch, and its backend process.</summary>
    public sealed record Captured(IReadOnlyList<Command> Commands, int ProcessId);

    /// <summary>True when <paramref name="text"/> may change or name a session setting, so its
    /// parameter values are kept.</summary>
    public static bool NamesSettings(string text) =>
        text.Contains("set_config", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("app.", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("SET ", StringComparison.OrdinalIgnoreCase);

    private static Command Of(string text, NpgsqlParameterCollection parameters) =>
        new(text, NamesSettings(text) ? Values(parameters) : Empty);

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
                activity.SetCustomProperty(Property, new Captured([Of(command.CommandText, command.Parameters)], ProcessOf(command.Connection))))
            .ConfigureBatchEnrichmentCallback((activity, batch) =>
                activity.SetCustomProperty(Property, new Captured(
                    Enumerable.Select<NpgsqlBatchCommand, Command>(batch.BatchCommands, c => Of(c.CommandText, c.Parameters)).ToList(), ProcessOf(batch.Connection))))
            .ConfigureCopyOperationEnrichmentCallback((activity, command) =>
                activity.SetCustomProperty(Property, new Captured([new Command(command, Empty)], 0))));

        private static int ProcessOf(NpgsqlConnection? connection)
        {
            try
            {
                return connection?.ProcessID ?? 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }
    }

    /// <summary>The capture an activity carries, if any.</summary>
    public static Captured? Of(Activity activity) => activity.GetCustomProperty(Property) as Captured;
}
