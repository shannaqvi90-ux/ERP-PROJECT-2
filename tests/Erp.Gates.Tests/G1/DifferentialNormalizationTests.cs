namespace Erp.Gates.Tests.G1;

/// <summary>The HTTP attack's differential check normalises both answers of a pair in one pass;
/// that pass must give exactly what normalising twice (once per value) gave.</summary>
public sealed class DifferentialNormalizationTests
{
    public static TheoryData<string> Bodies => new()
    {
        """{"items":[{"id":"0190a000-0000-7000-8000-00000000000b","name":"Bravo Trading","email":"admin@bravo.example"}],"total":1,"traceId":"00-abc-01"}""",
        """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"traceId":"00-1234-5678-00","errors":{"q":["Bravo is not \"known\""]}}""",
        """[{"a":1.50,"b":1e3,"c":-0,"d":true,"e":false,"f":null,"g":"x<y>&'+"},[],{},"bravo%40x.example","Bravo%20Trading"]""",
        """{"nested":{"traceId":"t","deeper":[{"traceId":"u","v":"BRAVO trading llc"}]},"traceIdent":"kept","TraceId":"kept too"}""",
        """{"name":"شركة برافو","note":"مرحبا bravo","emoji":"😀","tab":"a\tb","quote":"\"bravo\""}""",
        "\"bravo trading\"",
        "null",
        "42",
        "",
        "not json at all: bravo trading, admin@bravo.example",
        """{"broken": bravo}""",
        """{"a":1} {"b":2}""",
        """{"bravo trading":"key is never scrubbed","x":"Bravo Trading"}""",
        """  {"spaced" : [ 1 , 2 ] , "s" : "admin@bravo.example" }  """,
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public void One_pass_gives_what_two_passes_gave(string body)
    {
        foreach (var (first, second) in new[] { ("Bravo Trading", "Qxmpd Trbsorx"), ("admin@bravo.example", "zxkt@qwert.example"), ("bravo", "x"), ("\"bravo\"", "<") })
        {
            var (onePass, twoPasses) = IsolationAttack.NormalizationsOf(body, first, second);
            Assert.Equal(twoPasses, onePass);
            (onePass, twoPasses) = IsolationAttack.NormalizationsOf(body, second, first);
            Assert.Equal(twoPasses, onePass);
        }
    }
}
