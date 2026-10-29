using Erp.Kernel.Data;
using Erp.Modules.Jobs.Contracts;
using Erp.Modules.Jobs.Engine;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Modules.Jobs.Tests;

public sealed class JobsFixture : IAsyncLifetime
{
    public ErpTestEnvironment Env { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Env = await ErpTestEnvironment.StartGateAsync(new Dictionary<string, string?> { ["Erp:Testing:ExtraModules"] = typeof(JobTestModule).AssemblyQualifiedName });

    public async ValueTask DisposeAsync() => await Env.DisposeAsync();

    public JobRunner Runner => Env.Factory.Services.GetRequiredService<JobRunner>();

    /// <summary>Queue a job in a workspace as another module would: inside a unit of work bound to it.</summary>
    public async Task<Guid> EnqueueAsync(SeedTenant tenant, string type, object? payload = null, DateTimeOffset? runAt = null, Guid? actor = null, int priority = 0, string? source = null)
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(tenant.Id, actor, actor is null ? "system" : ErpDbSession.UserActorKind);
        var ticket = await scope.ServiceProvider.GetRequiredService<IJobQueue>().EnqueueAsync(new JobRequest(type, payload, null, runAt, priority, source));
        await session.CommitAsync();
        return ticket.Id;
    }

    /// <summary>Run <paramref name="action"/> with the queue of a workspace and commit.</summary>
    public async Task<T> WithQueueAsync<T>(SeedTenant tenant, Func<IJobQueue, Task<T>> action, bool commit = true)
    {
        await using var scope = Env.Factory.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<ErpDbSession>();
        await session.BeginAsync(tenant.Id, null, "system");
        var result = await action(scope.ServiceProvider.GetRequiredService<IJobQueue>());
        if (commit)
        {
            await session.CommitAsync();
        }
        else
        {
            await session.RollbackAsync();
        }
        return result;
    }

    public async Task<JobState> StateAsync(Guid id)
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("""
            SELECT status, attempts, max_attempts, progress, progress_en, error_code, error_en, error_ar, result::text, output_name, run_at, finished_at,
                   cancel_requested, locked_by, source, rerun_of, schedule_id, tenant_id
              FROM jobs.jobs WHERE id = @id
            """, admin);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the job does not exist");
        return new JobState(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6), reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10), reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11), reader.GetBoolean(12),
            reader.IsDBNull(13) ? null : reader.GetString(13), reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetGuid(15),
            reader.IsDBNull(16) ? null : reader.GetGuid(16), reader.GetGuid(17));
    }

    public async Task<IReadOnlyList<(int Number, string Outcome, string? Code)>> AttemptsAsync(Guid id)
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand("SELECT number, outcome, error_code FROM jobs.job_attempts WHERE job_id = @id ORDER BY number", admin);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        var list = new List<(int, string, string?)>();
        while (await reader.ReadAsync())
        {
            list.Add((reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return list;
    }

    public async Task SqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(sql, admin);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var admin = await Env.OpenAdminAsync();
        await using var command = new NpgsqlCommand(sql, admin);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Make a queued job due now (the wait after a failure is a real 30 seconds or more).</summary>
    public Task MakeDueAsync(Guid id) => SqlAsync("UPDATE jobs.jobs SET run_at = now() - interval '1 second' WHERE id = @id", ("id", id));
}

public sealed record JobState(
    string Status, int Attempts, int MaxAttempts, int Progress, string ProgressEn, string? ErrorCode, string ErrorEn, string ErrorAr, string? Result, string? OutputName,
    DateTimeOffset RunAt, DateTimeOffset? FinishedAt, bool CancelRequested, string? LockedBy, string Source, Guid? RerunOf, Guid? ScheduleId, Guid TenantId);
