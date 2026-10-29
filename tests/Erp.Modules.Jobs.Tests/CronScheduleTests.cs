using Erp.Modules.Jobs.Engine;

namespace Erp.Modules.Jobs.Tests;

public sealed class CronScheduleTests
{
    private static readonly TimeZoneInfo Dubai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("* * * * *")]
    [InlineData("30 3 * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 9-17 * * 1-5")]
    [InlineData("0 0 1,15 * *")]
    [InlineData("0 0 * jan,jul mon")]
    [InlineData("5/10 * * * *")]
    [InlineData("0 0 * * 7")]
    [InlineData("  0   0 * * 0  ")]
    public void Valid_timings_parse(string text) => Assert.True(CronSchedule.TryParse(text, out var schedule, out var error) && schedule is not null && error is null);

    [Theory]
    [InlineData("", "cronFields")]
    [InlineData("* * * *", "cronFields")]
    [InlineData("* * * * * *", "cronFields")]
    [InlineData("60 * * * *", "cronField")]
    [InlineData("* 24 * * *", "cronField")]
    [InlineData("* * 0 * *", "cronField")]
    [InlineData("* * 32 * *", "cronField")]
    [InlineData("* * * 13 *", "cronField")]
    [InlineData("* * * * 8", "cronField")]
    [InlineData("*/0 * * * *", "cronField")]
    [InlineData("5-1 * * * *", "cronField")]
    [InlineData("a * * * *", "cronField")]
    [InlineData("* * * foo *", "cronField")]
    [InlineData("0 0 31 2 *", "cronNever")]
    [InlineData("0 0 30 feb *", "cronNever")]
    public void Invalid_timings_are_refused_with_a_code(string text, string code)
    {
        Assert.False(CronSchedule.TryParse(text, out var schedule, out var error));
        Assert.Null(schedule);
        Assert.Equal(code, error);
    }

    [Fact]
    public void A_timing_longer_than_the_limit_is_refused() =>
        Assert.False(CronSchedule.TryParse(new string(' ', 200) + "* * * * *", out _, out var error) && error is null);

    [Theory]
    [InlineData("30 3 * * *", "2026-10-29T10:00:00+00:00", "2026-10-30T03:30:00+04:00")]
    [InlineData("30 3 * * *", "2026-10-29T03:29:59+04:00", "2026-10-29T03:30:00+04:00")]
    [InlineData("30 3 * * *", "2026-10-29T03:30:00+04:00", "2026-10-30T03:30:00+04:00")]
    [InlineData("*/15 * * * *", "2026-10-29T10:07:00+04:00", "2026-10-29T10:15:00+04:00")]
    [InlineData("0 9 * * 1-5", "2026-10-30T10:00:00+04:00", "2026-11-02T09:00:00+04:00")]
    [InlineData("0 0 1 * *", "2026-12-15T00:00:00+04:00", "2027-01-01T00:00:00+04:00")]
    [InlineData("0 0 29 2 *", "2026-10-29T00:00:00+04:00", "2028-02-29T00:00:00+04:00")]
    [InlineData("0 12 * * sun", "2026-10-29T00:00:00+04:00", "2026-11-01T12:00:00+04:00")]
    [InlineData("0 0 * * 7", "2026-10-29T00:00:00+04:00", "2026-11-01T00:00:00+04:00")]
    [InlineData("0 8 13 * fri", "2026-10-29T00:00:00+04:00", "2026-10-30T08:00:00+04:00")]
    public void The_next_moment_is_the_first_one_after_the_given_time_in_the_workspaces_zone(string timing, string after, string expected)
    {
        var next = CronSchedule.Parse(timing).NextAfter(At(after), Dubai);
        Assert.Equal(At(expected).ToUniversalTime(), next);
        Assert.Equal(TimeSpan.Zero, next!.Value.Offset);
    }

    [Fact]
    public void A_time_skipped_when_clocks_go_forward_does_not_run_that_day_and_a_repeated_one_runs_once()
    {
        // London, 28 March 2027: 01:00 jumps to 02:00; 31 October 2027: 02:00 goes back to 01:00.
        var skipped = CronSchedule.Parse("30 1 * * *").NextAfter(At("2027-03-28T00:00:00+00:00"), London);
        Assert.Equal(At("2027-03-29T00:30:00+00:00"), skipped);
        var first = CronSchedule.Parse("30 1 * * *").NextAfter(At("2027-10-31T00:00:00+00:00"), London);
        Assert.Equal(At("2027-10-31T00:30:00+00:00"), first); // the first of the two 01:30s
        var after = CronSchedule.Parse("30 1 * * *").NextAfter(first!.Value, London);
        Assert.Equal(At("2027-11-01T01:30:00+00:00"), after);
    }

    [Fact]
    public void The_backoff_doubles_from_thirty_seconds_up_to_an_hour_and_spreads_by_at_most_ten_percent()
    {
        var job = Guid.NewGuid();
        var waits = Enumerable.Range(1, 12).Select(n => JobBackoff.After(n, job)).ToList();
        for (var n = 0; n < waits.Count; n++)
        {
            var baseWait = TimeSpan.FromTicks(Math.Min(TimeSpan.FromSeconds(30).Ticks << n, TimeSpan.FromHours(1).Ticks));
            Assert.InRange(waits[n].Ticks, baseWait.Ticks, (long)(baseWait.Ticks * 1.1001));
        }
        Assert.True(waits[^1] >= TimeSpan.FromHours(1));
        Assert.True(waits[1] > waits[0]);
        Assert.NotEqual(JobBackoff.After(1, Guid.Parse("00000000-0000-0000-0000-000000000001")), JobBackoff.After(1, Guid.Parse("00000000-0000-0000-0000-0000000003e7")));
    }
}
