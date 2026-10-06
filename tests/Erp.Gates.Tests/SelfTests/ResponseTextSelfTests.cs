using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>The isolation gate reads exports as a reader would; these prove it sees text the raw
/// bytes hide, and that a declared print stamp is replaced only when it is a real, recent instant.</summary>
public sealed class ResponseTextSelfTests
{
    [Fact]
    public void A_compressed_pdf_and_a_zipped_workbook_are_read_for_their_text()
    {
        const string secret = "CNRYHIDDEN42 Victim Trading";
        var pdf = PlantedExports.Pdf(secret);
        Assert.DoesNotContain(secret, Encoding.Latin1.GetString(pdf), StringComparison.Ordinal);
        Assert.Contains(secret, ResponseText.Decode(pdf, "application/pdf"), StringComparison.Ordinal);
        var xlsx = PlantedExports.Xlsx("Victim & Sons " + secret);
        Assert.DoesNotContain(secret, Encoding.Latin1.GetString(xlsx), StringComparison.Ordinal);
        Assert.Contains("Victim & Sons " + secret, ResponseText.Decode(xlsx, "application/octet-stream"), StringComparison.Ordinal);
        Assert.Contains(secret, ObservedBody.Decode(Erp.Kernel.Security.IsolationProbeResult.Body("application/pdf", pdf)), StringComparison.Ordinal);
    }

    [Fact]
    public void Right_to_left_runs_are_also_given_in_logical_order()
    {
        Assert.Equal("abc النور 12", ResponseText.Logical("abc رونلا 12"));
    }

    [Fact]
    public async Task Only_a_recent_instant_and_a_short_dated_text_are_scrubbed_as_the_print_stamp()
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        using var good = Response($"report printed {now} at ٤ أكتوبر ٢٠٢٦ ١٧:٣١ by Admin", $"{now};{Uri.EscapeDataString("٤ أكتوبر ٢٠٢٦ ١٧:٣١")}");
        Assert.Equal("report printed <printed-at> at <printed-at> by Admin", await ResponseText.ReadAsync(good));

        // A stamp far from now, or a "stamp" that is really a long text or has no digits, scrubs nothing.
        var old = DateTimeOffset.UtcNow.AddDays(-30).ToString("O");
        using var stale = Response($"printed {old} CNRYSECRET", $"{old};CNRYSECRET");
        Assert.Contains("CNRYSECRET", await ResponseText.ReadAsync(stale), StringComparison.Ordinal);
        using var wordy = Response("printed CNRYSECRET", $"{now};CNRYSECRET");
        Assert.Contains("CNRYSECRET", await ResponseText.ReadAsync(wordy), StringComparison.Ordinal);
    }

    private static HttpResponseMessage Response(string body, string stamp)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
        response.Headers.TryAddWithoutValidation(ResponseText.PrintedAtHeader, stamp);
        return response;
    }
}
