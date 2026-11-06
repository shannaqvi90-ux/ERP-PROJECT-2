using Erp.Kernel.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Modules.Jobs.Engine;

/// <summary>
/// <c>Erp.Host jobs work</c>: the process that runs background jobs (the compose <c>worker</c> service).
/// It polls, takes up to <c>Erp:Jobs:Concurrency</c> (default 4) jobs at a time and starts each on its
/// own task, so a long job never holds up the next one. <c>Erp:Jobs:PollSeconds</c> (default 2) is the
/// wait when there was nothing to do. When the process is stopped, running jobs are asked to stop and go
/// back to the queue without losing a try. It needs the application role's connection string only (workspaces with work are found through
/// the reviewed function in <see cref="JobsSql"/>).
/// </summary>
internal static class JobWorkerCommand
{
    public const string ConcurrencySetting = "Erp:Jobs:Concurrency";
    public const string PollSetting = "Erp:Jobs:PollSeconds";

    public static readonly ModuleCommand Definition = new("jobs", "jobs work   (runs background jobs until stopped)", RunAsync);

    private static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> args, CancellationToken stopping)
    {
        if (args.Count != 1 || args[0] != "work")
        {
            Console.Error.WriteLine("Usage: jobs work");
            return 2;
        }
        var configuration = services.GetRequiredService<IConfiguration>();
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("Erp.Jobs.Worker");
        var runner = services.GetRequiredService<JobRunner>();
        log.LogInformation("Job worker {Worker} started.", runner.WorkerId);
        await RunLoopAsync(runner, configuration, log, stopping);
        log.LogInformation("Job worker {Worker} stopped.", runner.WorkerId);
        return 0;
    }

    /// <summary>The loop itself, until <paramref name="stopping"/> is cancelled.</summary>
    internal static async Task RunLoopAsync(JobRunner runner, IConfiguration configuration, ILogger log, CancellationToken stopping)
    {
        var concurrency = Math.Clamp(configuration.GetValue<int?>(ConcurrencySetting) ?? 4, 1, 32);
        var poll = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>(PollSetting) ?? 2, 1, 60));
        var running = new List<Task>();
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
