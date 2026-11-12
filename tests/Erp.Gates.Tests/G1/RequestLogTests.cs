namespace Erp.Gates.Tests.G1;

/// <summary>Self-test of <see cref="RequestLog"/>: a gate request that times out fails the test and
/// the failure names its label, method and path with the query, and lists the slowest requests
/// seen before it. Nothing is retried and the timeout is not touched.</summary>
public sealed class RequestLogTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    [Fact]
    public async Task A_timed_out_request_fails_naming_its_label_method_and_path_and_lists_the_slowest_before_it()
    {
        RequestLog.UsePrivateLog();
        var calls = 0;
        using var client = new HttpClient(new Handler(async request =>
        {
            Interlocked.Increment(ref calls);
            if (request.RequestUri!.AbsolutePath.EndsWith("/hangs", StringComparison.Ordinal))
            {
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
            }
            await Task.Delay(30);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        })) { BaseAddress = new Uri("http://localhost") };
        using (await client.GetTimedAsync("/api/self-test/slow-one?take=5", "request-log self-test slow")) { }
        var failure = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/self-test/hangs?x=1");
            using var _ = await client.SendTimedAsync(request, "attack tenant A selftest.hangs");
        });
        Assert.Contains("[attack tenant A selftest.hangs]", failure.Message, StringComparison.Ordinal);
        Assert.Contains("POST /api/self-test/hangs?x=1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("GET /api/self-test/slow-one?take=5", failure.Message, StringComparison.Ordinal);
        Assert.IsType<TaskCanceledException>(failure.InnerException);
        Assert.Equal(2, calls);
    }
}
