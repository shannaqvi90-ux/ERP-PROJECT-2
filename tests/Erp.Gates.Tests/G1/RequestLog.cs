using System.Diagnostics;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// Names the request when a gate request times out or fails, and remembers how long the slowest
/// ones took. Never changes the client's timeout, never retries, never skips: a request past the
/// HttpClient's limit still fails the test, but the failure says which request it was and lists the
/// slowest requests seen before it (path and seconds).
/// </summary>
internal static class RequestLog
{
    private const int Kept = 200;
    private static readonly object Lock = new();
    private static readonly List<(string Line, double Seconds)> Slowest = [];

    /// <summary>Sends the request like <see cref="HttpClient.SendAsync(HttpRequestMessage)"/>; a timeout or
    /// transport failure is rethrown naming <paramref name="label"/>, the method and the path with its query.</summary>
    public static async Task<HttpResponseMessage> SendTimedAsync(this HttpClient client, HttpRequestMessage request, string label)
    {
        var started = Stopwatch.StartNew();
        try
        {
            var response = await client.SendAsync(request);
            Record(label, request, started.Elapsed.TotalSeconds);
            return response;
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or HttpRequestException or IOException)
        {
            var seconds = started.Elapsed.TotalSeconds;
            Record(label + " (FAILED)", request, seconds);
            throw new TimeoutException(
                $"Request failed after {seconds:0.0} s: [{label}] {request.Method} {request.RequestUri?.OriginalString} " +
                $"({e.GetType().Name}: {e.Message}). Slowest requests seen before it:\n{Describe(20)}", e);
        }
    }

    public static Task<HttpResponseMessage> GetTimedAsync(this HttpClient client, string uri, string label) =>
        client.SendTimedAsync(new HttpRequestMessage(HttpMethod.Get, uri), label);

    private static void Record(string label, HttpRequestMessage request, double seconds)
    {
        var line = $"[{label}] {request.Method} {request.RequestUri?.OriginalString}";
        lock (Lock)
        {
            if (Slowest.Count >= Kept && seconds <= Slowest[^1].Seconds)
            {
                return;
            }
            Slowest.Add((line, seconds));
            Slowest.Sort((x, y) => y.Seconds.CompareTo(x.Seconds));
            if (Slowest.Count > Kept)
            {
                Slowest.RemoveAt(Slowest.Count - 1);
            }
        }
    }

    /// <summary>The slowest <paramref name="count"/> requests so far, one per line: seconds, label, method, path.</summary>
    public static string Describe(int count)
    {
        lock (Lock)
        {
            return string.Join('\n', Slowest.Take(count).Select(s => $"  {s.Seconds,7:0.0} s  {s.Line}"));
        }
    }
}
