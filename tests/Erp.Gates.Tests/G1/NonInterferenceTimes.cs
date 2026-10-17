using System.Globalization;
using System.Text.RegularExpressions;
using Erp.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// Times and cookie values in the answers the non-interference check compares (critic p04 round 9:
/// every time and every cookie value used to be blanked, so a process-wide count sent in a cookie,
/// or the time of the other tenant's last action sent in a header or a body, passed every G1
/// check).
/// <para>
/// A time is no longer blanked; it is attributed. Every request the check sends is timed (its
/// window, from just before it is sent to just after its answer is read), and so is each tenant's
/// own writing before the comparison. A time in an answer that falls inside the window of the
/// request being answered is <c>&lt;time:this&gt;</c> (a stamp the answer makes, an updated-at of
/// the write itself); inside a window of an earlier request of the same tenant,
/// <c>&lt;time:own&gt;</c>; inside a window of the other tenant's,
/// <c>&lt;time:other&gt;</c>. A time inside no window (a seeded record's, a cookie's expiry, a
/// sign-in before the run) is <c>&lt;time&gt;</c>, the same in both answers because they read one
/// database. So an answer that tells the judged tenant when the other tenant last did something
/// reads <c>&lt;time:other&gt;</c> where a process only the judged tenant used reads
/// <c>&lt;time:own&gt;</c>, and the comparison reports it; times that differ only because the two
/// answers were made at different moments still compare equal.
/// </para>
/// <para>
/// A time is read with its precision (a fraction's last digit, a whole second, a minute) and the
/// database clock's measured distance from the test's: when that interval touches windows of more
/// than one kind, it is <c>&lt;time:ambiguous&gt;</c>, and the comparison it was in is run once more
/// with the requests paced apart by more than that interval, so a second-precision time is
/// attributed as well. Times coarser than a second are not attributed.
/// </para>
/// Cookie values are compared as sent, except the values of the product's two credential cookies
/// (the session and the trusted-device cookie), which are new on every sign-in by design.
/// </summary>
public static partial class NonInterference
{
    /// <summary>The product's credential cookies, read from the product: the session cookie and
    /// the trusted-device cookie (an internal type of the identity module).</summary>
    internal static readonly IReadOnlySet<string> CredentialCookies = new HashSet<string>(StringComparer.Ordinal)
    {
        Erp.Kernel.Security.SessionAuthenticationDefaults.CookieName,
        (string)typeof(Erp.Modules.Identity.IdentityModule).Assembly.GetType("Erp.Modules.Identity.Auth.TrustedDevices", throwOnError: true)!
            .GetField("CookieName")!.GetRawConstantValue()!,
    };

    /// <summary>One timed request (or one tenant's writing before the comparison).</summary>
    internal sealed record Window(DateTimeOffset Start, DateTimeOffset End, char Tenant);

    /// <summary>An answer being normalised: the clock and the request it answers.</summary>
    internal sealed record AnswerContext(RequestClock Clock, Window Current);

    /// <summary>The windows of every request the check sent, in order, and what was attributed.</summary>
    internal sealed class RequestClock
    {
        private readonly List<Window> _windows = [];
        private DateTimeOffset _lastEnd = DateTimeOffset.MinValue;

        /// <summary>How far a time the database stamped may sit from the test's clock: the
        /// measured offset of the database clock plus half the round trip that measured it.</summary>
        public TimeSpan Tolerance { get; set; }

        /// <summary>When positive, each request waits until this long after the previous one
        /// ended (the re-run of a comparison that met an ambiguous time).</summary>
        public TimeSpan Pace { get; set; }

        public int TimesAttributed { get; private set; }
        public int TimesAmbiguous { get; private set; }
        public int CookieValuesCompared { get; set; }
        public int PacedRuns { get; set; }

        /// <summary>The widest interval an ambiguous time has had since this was last reset.</summary>
        public TimeSpan WidestAmbiguous { get; set; }

        public async Task<DateTimeOffset> StartAsync()
        {
            if (Pace > TimeSpan.Zero && _lastEnd != DateTimeOffset.MinValue)
            {
                var wait = _lastEnd + Pace - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait);
                }
            }
            return DateTimeOffset.UtcNow;
        }

        /// <summary>Records a request that started at <paramref name="start"/> and ends now.</summary>
        public AnswerContext End(DateTimeOffset start, char tenant)
        {
            var window = Add(start, DateTimeOffset.UtcNow, tenant);
            return new AnswerContext(this, window);
        }

        public Window Add(DateTimeOffset start, DateTimeOffset end, char tenant)
        {
            var window = new Window(start, end < start ? start : end, tenant);
            // Requests are sent one after another, so windows arrive in order; keep them sorted
            // anyway so the search below stays right.
            var at = _windows.Count;
            while (at > 0 && _windows[at - 1].Start > window.Start)
            {
                at--;
            }
            _windows.Insert(at, window);
            if (window.End > _lastEnd)
            {
                _lastEnd = window.End;
            }
            return window;
        }

        /// <summary>The token a time in an answer to <paramref name="current"/> is compared as.</summary>
        public string Token(string text, Window current)
        {
            if (!TryRead(text, out var candidates, out var precision) || precision > TimeSpan.FromSeconds(1))
            {
                return "<time>";
            }
            var reach = precision + Tolerance;
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var at in candidates)
            {
                foreach (var window in Touching(at - reach, at + reach))
                {
                    kinds.Add(ReferenceEquals(window, current) ? "this"
                        : window.Tenant == '?' || current.Tenant == '?' ? "unknown"
                        : window.Tenant == current.Tenant ? "own" : "other");
                }
            }
            if (kinds.Count == 0 || (kinds.Count == 1 && kinds.Contains("unknown")))
            {
                return "<time>";
            }
            if (kinds.Count == 1)
            {
                TimesAttributed++;
                return $"<time:{kinds.Single()}>";
            }
            TimesAmbiguous++;
            var width = reach + reach;
            if (width > WidestAmbiguous)
            {
                WidestAmbiguous = width;
            }
            return "<time:ambiguous>";
        }

        private IEnumerable<Window> Touching(DateTimeOffset from, DateTimeOffset to)
        {
            // Windows do not overlap and are sorted, so their ends are sorted too: the first
            // window ending at or after 'from', then on while windows start by 'to'.
            int low = 0, high = _windows.Count;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (_windows[mid].End < from)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }
            for (var i = low; i < _windows.Count && _windows[i].Start <= to; i++)
            {
                yield return _windows[i];
            }
        }

        /// <summary>Reads an ISO 8601 or HTTP date-time with its precision. A time without an
        /// offset may be in UTC, in the Gulf (+04:00, the tenants' zone) or in the machine's zone,
        /// so it is read all three ways.</summary>
        internal static bool TryRead(string text, out IReadOnlyList<DateTimeOffset> candidates, out TimeSpan precision)
        {
            candidates = [];
            precision = TimeSpan.Zero;
            var western = Western(text);
            if (DateTimeOffset.TryParseExact(western, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var http))
            {
                candidates = [http];
                precision = TimeSpan.FromSeconds(1);
                return true;
            }
            var parts = IsoParts().Match(western);
            if (!parts.Success)
            {
                return false;
            }
            var fraction = parts.Groups["fraction"].Value;
            precision = fraction.Length > 0 ? TimeSpan.FromTicks((long)Math.Pow(10, Math.Max(0, 7 - fraction.Length)))
                : parts.Groups["seconds"].Success ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1);
            if (fraction.Length > 7)
            {
                western = western.Remove(parts.Groups["fraction"].Index + 7, fraction.Length - 7);
            }
            if (parts.Groups["offset"].Success)
            {
                if (!DateTimeOffset.TryParse(western, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exact))
                {
                    return false;
                }
                candidates = [exact];
                return true;
            }
            if (!DateTime.TryParse(western, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            {
                return false;
            }
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            candidates = new[] { TimeSpan.Zero, TimeSpan.FromHours(4), TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow) }
                .Distinct().Select(offset => new DateTimeOffset(unspecified, offset)).ToList();
            return true;
        }
    }

    /// <summary>Measures how far the database's clock is from the test's (most stamps a write
    /// answers are the database's transaction time), from the sample with the shortest round trip.</summary>
    internal static async Task<TimeSpan> DatabaseClockToleranceAsync(ErpTestEnvironment env)
    {
        await using var connection = await env.OpenAdminAsync();
        var best = TimeSpan.MaxValue;
        var offset = TimeSpan.Zero;
        for (var i = 0; i < 7; i++)
        {
            await using var command = new Npgsql.NpgsqlCommand("SELECT clock_timestamp()", connection);
            var before = DateTimeOffset.UtcNow;
            var database = (DateTime)(await command.ExecuteScalarAsync())!;
            var after = DateTimeOffset.UtcNow;
            var trip = after - before;
            if (trip < best)
            {
                best = trip;
                offset = new DateTimeOffset(DateTime.SpecifyKind(database, DateTimeKind.Utc)) - (before + trip / 2);
            }
        }
        return offset.Duration() + best / 2;
    }

    /// <summary>A time replaced by its token (<see cref="RequestClock.Token"/>), or by
    /// <c>&lt;time&gt;</c> when no request context is known.</summary>
    private static string TimeToken(Match match, AnswerContext? context) =>
        context is null ? "<time>" : context.Clock.Token(match.Value, context.Current);

    /// <summary>A Set-Cookie value with each credential cookie's value replaced; every other
    /// cookie's value is part of the answer.</summary>
    private static string CookieValues(string value, AnswerContext? context) =>
        CookieValueRegex().Replace(value, m =>
        {
            if (CredentialCookies.Contains(m.Groups[1].Value))
            {
                return $"{m.Groups[1].Value}=<value>";
            }
            if (context is not null)
            {
                context.Clock.CookieValuesCompared++;
            }
            return m.Value;
        });

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?<seconds>:\d{2}(?:\.(?<fraction>\d+))?)?(?<offset>Z|[+-]\d{2}:\d{2})?$")]
    private static partial Regex IsoParts();
}
