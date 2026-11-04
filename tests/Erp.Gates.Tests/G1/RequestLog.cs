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
    private sealed class Sink
    {
        public readonly object Lock = new();
        public readonly List<(string Line, double Seconds)> Slowest = [];
    }

    private static readonly Sink Shared = new();
    private static readonly AsyncLocal<Sink?> Private = new();
    private static Sink Current => Private.Value ?? Shared;

    /// <summary>For the log's own self-test: requests sent from this async flow are kept apart from the
    /// gate's, which other tests of the same process fill at the same time.</summary>
    public static void UsePrivateLog() => Private.Value = new Sink();

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
                $"Request failed after {seconds:0.0} s: [{label}] {request.Method} {PathOf(request)} " +
                $"({e.GetType().Name}: {e.Message}). Slowest requests seen before it:\n{Describe(20)}", e);
        }
    }

    public static Task<HttpResponseMessage> GetTimedAsync(this HttpClient client, string uri, string label) =>
        client.SendTimedAsync(new HttpRequestMessage(HttpMethod.Get, uri), label);

    private static string PathOf(HttpRequestMessage request) =>
        request.RequestUri is { IsAbsoluteUri: true } uri ? uri.PathAndQuery : request.RequestUri?.OriginalString ?? "";

    private static void Record(string label, HttpRequestMessage request, double seconds)
    {
        var line = $"[{label}] {request.Method} {PathOf(request)}";
        var sink = Current;
        lock (sink.Lock)
        {
            var slowest = sink.Slowest;
            if (slowest.Count >= Kept && seconds <= slowest[^1].Seconds)
            {
                return;
            }
            slowest.Add((line, seconds));
            slowest.Sort((x, y) => y.Seconds.CompareTo(x.Seconds));
            if (slowest.Count > Kept)
            {
                slowest.RemoveAt(slowest.Count - 1);
            }
        }
    }

    /// <summary>The slowest <paramref name="count"/> requests so far, one per line: seconds, label, method, path.</summary>
    public static string Describe(int count)
    {
        var sink = Current;
        lock (sink.Lock)
        {
            return string.Join('\n', sink.Slowest.Take(count).Select(s => $"  {s.Seconds,7:0.0} s  {s.Line}"));
        }
    }
}
