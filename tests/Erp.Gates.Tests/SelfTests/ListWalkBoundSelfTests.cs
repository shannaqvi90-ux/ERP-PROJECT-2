using System.Net;
using System.Text;
using Erp.Gates.Tests.G1;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>The whole-list walk's page bound follows the list's own total: a long list is walked to
/// its end whatever volume the caller named, and a cursor that never ends still stops.</summary>
public sealed class ListWalkBoundSelfTests
{
    private sealed class Pages(int total, int take, bool endless) : HttpMessageHandler
    {
        public int Asked;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var page = Interlocked.Increment(ref Asked) - 1;
            var rows = Enumerable.Range(page * take, take).Select(i => $"{{\"id\":\"{i}\"}}");
            var last = !endless && (page + 1) * take >= total;
            var body = $"{{\"items\":[{string.Join(",", rows)}],\"total\":{total},\"next\":{(last ? "null" : "\"c" + page + "\"")}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("http://list.test") };

    [Fact]
    public async Task A_list_longer_than_the_named_volume_is_walked_to_its_last_row()
    {
        // 6,000 rows of 2 per page = 3,000 pages, far past a floor of 5 pages and past 2 x volume / take + 1000 of a small volume.
        using var a = Client(new Pages(6000, 2, endless: false));
        using var b = Client(new Pages(6000, 2, endless: false));
        var (walkA, walkB) = await ListAnswers.WalkTogetherAsync(a, b, "/api/things", "", 2, maxPages: 5);
        Assert.NotNull(walkA);
        Assert.NotNull(walkB);
        Assert.Equal(6000, walkA!.Rows.Count);
        Assert.Equal(6000, walkB!.Rows.Count);
    }

    [Fact]
    public async Task A_cursor_that_never_ends_still_stops()
    {
        var handler = new Pages(10, 2, endless: true);
        using var a = Client(handler);
        using var b = Client(new Pages(10, 2, endless: false));
        var (walkA, _) = await ListAnswers.WalkTogetherAsync(a, b, "/api/things", "", 2, maxPages: 5);
        Assert.Null(walkA);
        Assert.InRange(handler.Asked, 100, 1100);
    }
}
