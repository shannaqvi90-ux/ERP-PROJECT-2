using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>Scrubbing what the attacker sent or stored never cuts another company's id out of an answer.</summary>
public sealed class MarkerScrubSelfTests
{
    private static string Replace(string text, string value) => text.Replace(value, "<sent>", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void A_stored_text_made_of_hex_digits_does_not_break_an_id_that_holds_it()
    {
        var id = "5a123f9c-20002-4bde-8e3d-0123456789ab".Replace("-20002-", "-1234-");
        var answer = $"[{{\"tenantId\":\"{id}\",\"name\":\"123 Main street\"}}]";
        var scrubbed = MarkerScrub.Scrub(answer, ["123", "1234", "dad", "20002"], Replace);
        Assert.Contains(id, scrubbed, StringComparison.Ordinal);
        Assert.Contains("<sent> Main street", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_the_request_sent_is_still_removed_whole_and_other_texts_are_removed_everywhere()
    {
        var sent = Guid.NewGuid().ToString();
        var other = Guid.NewGuid().ToString();
        var scrubbed = MarkerScrub.Scrub($"{sent} {other} {sent.ToUpperInvariant()} Dubai dubai", [sent, "dubai"], Replace);
        Assert.DoesNotContain(sent, scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dubai", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(other, scrubbed, StringComparison.Ordinal);
    }
}
