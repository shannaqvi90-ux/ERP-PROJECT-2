namespace Erp.Gates.Tests.Infrastructure;

/// <summary>Every header of a response (message and content headers, including Location,
/// Set-Cookie, ETag and Content-Disposition) as text, one per line, so the isolation gate judges
/// them exactly like the body: data can leave through any of them.</summary>
public static class ResponseHeaders
{
    public static string Text(HttpResponseMessage response) =>
        string.Join("\n", response.Headers.Concat(response.Content.Headers)
            .Select(h => h.Key + ": " + string.Join(", ", h.Value)));
}
