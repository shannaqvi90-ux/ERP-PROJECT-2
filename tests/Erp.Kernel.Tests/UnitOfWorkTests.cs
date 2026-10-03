using Erp.Kernel.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Erp.Kernel.Tests;

/// <summary>The request transaction commits only for success results, including results held
/// inside typed unions (a problem inside <c>Results&lt;…&gt;</c> must roll back).</summary>
public sealed class UnitOfWorkTests
{
    [Fact]
    public void Plain_results_report_their_own_status()
    {
        Assert.Equal(200, UnitOfWorkFilter.StatusOf(TypedResults.Ok("x")));
        Assert.Equal(204, UnitOfWorkFilter.StatusOf(TypedResults.NoContent()));
        Assert.Equal(409, UnitOfWorkFilter.StatusOf(TypedResults.Problem(new ProblemDetails { Status = 409 })));
        Assert.Equal(200, UnitOfWorkFilter.StatusOf("a plain value"));
        Assert.Equal(200, UnitOfWorkFilter.StatusOf(null));
    }

    [Fact]
    public void Problems_inside_typed_unions_count_as_failures()
    {
        Results<Ok<string>, ProblemHttpResult> failure = TypedResults.Problem(new ProblemDetails { Status = StatusCodes.Status400BadRequest });
        Results<Ok<string>, ProblemHttpResult> success = TypedResults.Ok("done");
        Results<Created<string>, NotFound, ProblemHttpResult> notFound = TypedResults.NotFound();
        Assert.Equal(400, UnitOfWorkFilter.StatusOf(failure));
        Assert.Equal(200, UnitOfWorkFilter.StatusOf(success));
        Assert.Equal(404, UnitOfWorkFilter.StatusOf(notFound));
    }
}
