using Erp.Kernel.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Jobs.Engine;

/// <summary>
/// The background service that runs jobs in the web application's process. It polls, takes up to
/// <c>Erp:Jobs:Concurrency</c> (default 4) jobs at a time and starts each on its own task, so a long
/// job never holds up the next one. <c>Erp:Jobs:PollSeconds</c> (default 2) is the wait when there
/// was nothing to do; <c>Erp:Jobs:Worker</c> set to false switches the worker off (tests and the
/// operator's migrate and seed commands). In the Testing environment it is off unless
/// <c>Erp:Jobs:Worker</c> is true. When the application stops, running jobs are asked to stop and
/// go back to the queue without losing a try.
/// </summary>
internal sealed class JobWorker(JobRunner runner, IConfiguration configuration, IHostEnvironment environment, ILogger<JobWorker> log) : BackgroundService
{
    public const string WorkerSetting = "Erp:Jobs:Worker";
    public const string ConcurrencySetting = "Erp:Jobs:Concurrency";
    public const string PollSetting = "Erp:Jobs:PollSeconds";

    internal static bool Enabled(IConfiguration configuration, IHostEnvironment environment) =>
        configuration.GetValue<bool?>(WorkerSetting) ?? !environment.IsEnvironment("Testing");

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        if (!Enabled(configuration, environment))
        {
            return;
        }
        var concurrency = Math.Clamp(configuration.GetValue<int?>(ConcurrencySetting) ?? 4, 1, 32);
        var poll = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>(PollSetting) ?? 2, 1, 60));
        var running = new List<Task>();
        // Let the application finish starting (migrations run before the first request).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stopping);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                running.RemoveAll(t => t.IsCompleted);
                var claimed = running.Count < concurrency ? await runner.ClaimAsync(concurrency - running.Count, stopping) : [];
                foreach (var job in claimed)
                {
                    running.Add(Task.Run(() => runner.ExecuteAsync(job, stopping), CancellationToken.None));
                }
                if (claimed.Count == 0 || running.Count >= concurrency)
                {
                    await Task.WhenAny(running.Append(Task.Delay(poll, stopping)));
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database may be down for a moment: look again.
                log.LogError(ex, "The job worker's pass failed.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stopping);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        await Task.WhenAll(running);
    }
}
