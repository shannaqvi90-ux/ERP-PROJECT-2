using System.Text;
using Erp.Kernel.Localization;
using Erp.Modules.Jobs.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Jobs.Handlers;

/// <summary>
/// Proves the queue works in this workspace: runs <c>steps</c> short steps (1 to 20, default 5, each
/// <c>pauseMilliseconds</c> long, 0 to 1000, default 200), reports its progress after each, honours
/// cancellation, and keeps a result and a small output file. People with the run permission start
/// it from the Background jobs screen.
/// </summary>
internal sealed class SelfCheckJobHandler : IJobHandler
{
    public const string TypeKey = "jobs.selfCheck";

    public static readonly JobType Type = new(TypeKey, "job.type.jobs.selfCheck", MaxAttempts: 1, Timeout: TimeSpan.FromMinutes(2),
        RunPermission: JobsPermissions.JobsRun, Description: "job.type.jobs.selfCheck.description", Manual: true, Schedulable: true);

    public async Task RunAsync(IJobContext context)
    {
        var steps = Number(context, "steps", 5, 1, 20);
        var pause = Number(context, "pauseMilliseconds", 200, 0, 1000);
        var strings = context.Services.GetRequiredService<StringCatalog>();
        var log = new StringBuilder();
        for (var step = 1; step <= steps; step++)
        {
            context.Cancellation.ThrowIfCancellationRequested();
            if (pause > 0)
            {
                await Task.Delay(pause, context.Cancellation);
            }
            await context.ReportAsync(step * 100 / steps, new JobText(
                strings.Get("job.selfCheck.step", Languages.English, step, steps),
                strings.Get("job.selfCheck.step", Languages.Arabic, step, steps)));
            log.Append("step ").Append(step).Append('/').Append(steps).Append(" ok\n");
        }
        context.SetResult(new { steps, pauseMilliseconds = pause });
        context.SetOutput("self-check.txt", "text/plain", log.ToString());
    }

    private static int Number(IJobContext context, string name, int fallback, int min, int max)
    {
        if (context.Payload.ValueKind != System.Text.Json.JsonValueKind.Object || !context.Payload.TryGetProperty(name, out var given))
        {
            return fallback;
        }
        if (given.ValueKind != System.Text.Json.JsonValueKind.Number || !given.TryGetInt32(out var value) || value < min || value > max)
        {
            throw new JobFailure("jobs.selfCheck.parameter",
                new JobText($"{name} must be a whole number from {min} to {max}.", $"يجب أن يكون {name} عددًا صحيحًا من {min} إلى {max}."), retry: false);
        }
        return value;
    }
}
