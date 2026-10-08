using Erp.Kernel.Data;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Erp.Kernel.Http;

/// <summary>
/// Commits the request's database transaction when the endpoint produced a success result, before
/// the response is written, and rolls back otherwise. Read-only requests (<see cref="ReadOnlyRequests"/>)
/// run in a read-only transaction (set by <see cref="ErpDbSession.BeginAsync"/>). An endpoint that must persist something on
/// a failure path (for example a failed sign-in counter) commits explicitly first.
/// </summary>
internal sealed class UnitOfWorkFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var session = context.HttpContext.RequestServices.GetRequiredService<ErpDbSession>();
        session.CorrelationId ??= context.HttpContext.TraceIdentifier;
        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await session.RollbackAsync();
            throw;
        }
        if (StatusOf(result) < 400)
        {
            await session.CommitAsync(context.HttpContext.RequestAborted);
        }
        else
        {
            await session.RollbackAsync();
        }
        return result;
    }

    /// <summary>The status an endpoint result will write. Typed unions such as
    /// <c>Results&lt;Ok&lt;T&gt;, ProblemHttpResult&gt;</c> are unwrapped to the result they hold, so
    /// a problem returned inside a union rolls the transaction back.</summary>
    internal static int StatusOf(object? result)
    {
        while (result is INestedHttpResult nested)
        {
            result = nested.Result;
        }
        return result is IStatusCodeHttpResult { StatusCode: { } code } ? code : StatusCodes.Status200OK;
    }
}

/// <summary>Maps exceptions to problem responses without leaking internals.</summary>
internal sealed class ErpExceptionHandler(ILogger<ErpExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is CrossTenantBindException)
        {
            logger.LogError(exception, "Request {TraceId} tried to bind another tenant", context.TraceIdentifier);
        }
        var (status, code) = Classify(exception, context.RequestAborted.IsCancellationRequested);
        if (status >= 500 || exception is CrossTenantWriteException or CrossCompanyWriteException or CrossBranchWriteException || exception is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege }
            || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege })
        {
            logger.LogError(exception, "Request {TraceId} failed with {Code}", context.TraceIdentifier, code);
        }
        else
        {
            logger.LogInformation("Request {TraceId} answered {Status} {Code}: {Message}", context.TraceIdentifier, status, code, exception.Message);
        }
        var session = context.RequestServices.GetService<ErpDbSession>();
        if (session is not null)
        {
            await session.RollbackAsync(CancellationToken.None);
        }
        if (context.Response.HasStarted)
        {
            return true;
        }
        await Problems.Write(context, status, code);
        return true;
    }

    /// <summary>The answer for an exception: never internals, and a 5xx only for faults.</summary>
    internal static (int Status, string Code) Classify(Exception exception, bool requestAborted)
    {
        var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;
        return exception switch
        {
            // Code serving a signed-in user tried to switch tenant: answer as if nothing was there.
            CrossTenantBindException => (StatusCodes.Status404NotFound, "notFound"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency"),
            // Two requests changed the same rows at the same moment and PostgreSQL chose this one
            // to give way (deadlock, serialization failure, lock not available): nothing was
            // saved, and trying again works. A conflict, not a fault.
            _ when postgres?.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.LockNotAvailable
                => (StatusCodes.Status409Conflict, "concurrency"),
            _ when postgres?.SqlState == PostgresErrorCodes.UniqueViolation => (StatusCodes.Status409Conflict, "duplicate"),
            // A referenced row that is not there (in this tenant): as if the target did not exist.
            _ when postgres?.SqlState == PostgresErrorCodes.ForeignKeyViolation => (StatusCodes.Status404NotFound, "notFound"),
            // A row-level security violation means code tried to write another tenant's row. Answer
            // as if the row did not exist, and log it loudly.
            _ when postgres?.SqlState == PostgresErrorCodes.InsufficientPrivilege => (StatusCodes.Status404NotFound, "notFound"),
            CrossTenantWriteException => (StatusCodes.Status404NotFound, "notFound"),
            CrossCompanyWriteException => (StatusCodes.Status404NotFound, "notFound"),
            // The user reads the record (it is in their company) but their branch limits forbid the
            // write: a refusal, with the reason.
            CrossBranchWriteException branch => (StatusCodes.Status403Forbidden, branch.Code),
            BadHttpRequestException bad => (bad.StatusCode, "request.malformed"),
            OperationCanceledException when requestAborted => (499, "request.cancelled"),
            _ => (StatusCodes.Status500InternalServerError, "internal"),
        };
    }
}
