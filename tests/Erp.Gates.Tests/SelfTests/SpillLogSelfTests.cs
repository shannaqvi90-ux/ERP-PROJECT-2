using Erp.Gates.Tests.Infrastructure;

namespace Erp.Gates.Tests.SelfTests;

/// <summary>The statement trace keeps its records in files: every record comes back, whole and in
/// order, from any position, however many threads wrote them.</summary>
public sealed class SpillLogSelfTests
{
    private static SpillLog<(string Text, long Number, object? App, bool? Flag)> NewLog() => new(
        (w, x) => { w.Text(x.Text); w.Write(x.Number); w.App(x.App); w.Flag(x.Flag); },
        r => (r.Text()!, r.ReadInt64(), r.App(), r.Flag()));

    [Fact]
    public void Every_record_comes_back_whole_and_in_order_from_any_position()
    {
        var log = NewLog();
        var app = new object();
        var other = new object();
        var long8k = new string('x', 7000);
        var huge = new string('y', 3_000_000); // larger than the write buffer
        for (var i = 0; i < 5000; i++)
        {
            log.Add((i % 7 == 0 ? long8k : i == 2500 ? huge : $"text {i % 13} ünï {i}", i, i % 2 == 0 ? app : other, i % 3 == 0 ? null : i % 3 == 1));
        }
        Assert.Equal(5000, log.Count);
        foreach (var since in new[] { 0, 1, 1023, 1024, 1025, 2500, 4999 })
        {
            var rows = log.From(since).ToList();
            Assert.Equal(5000 - since, rows.Count);
            for (var k = 0; k < rows.Count; k++)
            {
                var i = since + k;
                Assert.Equal(i, rows[k].Number);
                Assert.Equal(i % 7 == 0 ? long8k : i == 2500 ? huge : $"text {i % 13} ünï {i}", rows[k].Text);
                Assert.Same(i % 2 == 0 ? app : other, rows[k].App);
                Assert.Equal(i % 3 == 0 ? null : i % 3 == 1, rows[k].Flag);
            }
        }
        Assert.Empty(log.From(5000));
    }

    [Fact]
    public async Task Records_written_by_many_threads_are_all_there_once()
    {
        var log = NewLog();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 4000; i++)
            {
                log.Add(($"thread {t}", t * 10_000L + i, null, null));
            }
        })));
        var rows = log.From(0).ToList();
        Assert.Equal(32_000, rows.Count);
        Assert.Equal(32_000, rows.Select(r => r.Number).Distinct().Count());
        foreach (var group in rows.GroupBy(r => r.Text))
        {
            Assert.Equal(4000, group.Count());
            Assert.Equal(group.Select(r => r.Number).Order(), group.Select(r => r.Number)); // each thread's records keep their order
        }
    }

    [Fact]
    public void A_text_written_for_every_record_is_read_back_as_one_string()
    {
        var log = NewLog();
        for (var i = 0; i < 100; i++)
        {
            log.Add((new string('t', 300) + "x", i, null, null));
        }
        var texts = log.From(0).Select(r => r.Text).ToList();
        Assert.All(texts, t => Assert.Same(texts[0], t));
    }
}
