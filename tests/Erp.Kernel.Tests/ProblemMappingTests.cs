using Erp.Kernel.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Kernel.Tests;

/// <summary>Exceptions become problem answers without internals, and a 5xx only for a fault: two
/// requests that collide on the same rows get a conflict they can retry, not a server error.</summary>
public sealed class ProblemMappingTests
{
    private static PostgresException Postgres(string sqlState) =>
        new("message", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData(PostgresErrorCodes.DeadlockDetected, 409, "concurrency")]
    [InlineData(PostgresErrorCodes.SerializationFailure, 409, "concurrency")]
    [InlineData(PostgresErrorCodes.LockNotAvailable, 409, "concurrency")]
    [InlineData(PostgresErrorCodes.UniqueViolation, 409, "duplicate")]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, 404, "notFound")]
    [InlineData(PostgresErrorCodes.InsufficientPrivilege, 404, "notFound")]
    [InlineData(PostgresErrorCodes.DivisionByZero, 500, "internal")]
    public void Database_errors_map_the_same_whether_EF_wraps_them_or_not(string sqlState, int status, string code)
    {
        Assert.Equal((status, code), ErpExceptionHandler.Classify(Postgres(sqlState), false));
        Assert.Equal((status, code), ErpExceptionHandler.Classify(new DbUpdateException("save", Postgres(sqlState)), false));
    }

    [Fact]
    public void Anything_unknown_is_an_internal_error_and_a_concurrent_save_is_a_conflict()
    {
        Assert.Equal((500, "internal"), ErpExceptionHandler.Classify(new InvalidOperationException("x"), false));
        Assert.Equal((409, "concurrency"), ErpExceptionHandler.Classify(new DbUpdateConcurrencyException("x"), false));
        Assert.Equal((499, "request.cancelled"), ErpExceptionHandler.Classify(new OperationCanceledException(), true));
        Assert.Equal((500, "internal"), ErpExceptionHandler.Classify(new OperationCanceledException(), false));
    }
}
