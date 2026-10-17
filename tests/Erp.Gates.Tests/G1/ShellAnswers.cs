using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Erp.Kernel.Seeding;
using Erp.Testing;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 on the shell's own answers, raw (critic p04 round 9, plants L4 and L6). The shell's server
/// surface is small: the session it reads on every start, sign-in and sign-out, and the user's own
/// preferences it writes on every language or digits switch. The shared non-interference check
/// (<see cref="NonInterference"/>) blanks every cookie value and every time before it compares, so a
/// count of every tenant's switches sent back as a cookie, or the time of another tenant's last
/// switch sent in a header, passed every G1 check while tenant A watched tenant B's activity.
/// <para>
/// This check judges every byte of those answers that the shell's endpoints send: the status, every
/// response header except the transport's own (<c>Date</c> and the like), every cookie's value and
/// attributes, and every leaf of the body. Only the values of the reviewed credential cookies
/// (<see cref="CredentialCookies"/>) are not compared, as they are fresh random tokens by design; their
/// attributes are. Nothing is blanked: each value is split into its text, its numbers and its times,
/// and judged by how it changes along one tenant's own history.
/// </para>
/// <para>
/// One tenant (X) makes the same turn (a preferences write and the session read that follows, or a
/// whole sign-in, session read and sign-out) at a fixed pace: a warm-up, then three turns alone, then
/// a fourth turn just after the other tenant (Y) made the same turn three times. Every value of X's
/// fourth turn must follow from X's own three turns alone: the same text; each number moved by the
/// same step as alone (a version X's own writes raise moves by one every turn either way; a count
/// every tenant's switches raise moves by four); each time either fixed, or at the same distance
/// from X's request as alone (X's turns are evenly paced, so "X's previous switch" stays four
/// seconds back, while "anyone's previous switch" moves to Y's, a moment back); and no time inside
/// Y's requests and outside X's own. A value that appears or disappears after Y's turns, or that
/// changes along X's own turns in a way none of these rules can judge, is a finding too unless it is
/// on the reviewed list of values random by design (<see cref="RandomByDesign"/>). Both directions,
/// every language and digits combination of the preferences write (Arabic sessions included).
/// </para>
/// </summary>
public static partial class ShellAnswers
{
    /// <summary>The shell's own endpoints (web/src/kernel/session.tsx and preferences.ts): every one
    /// is judged, and <see cref="G1ShellAnswersTests"/> requires that the shell calls no other.</summary>
    public static readonly (string Method, string Path)[] Endpoints =
    [
        ("PUT", ArabicSession.PreferencesPath),
        ("GET", "/api/auth/session"),
        ("POST", "/api/auth/sign-in"),
        ("POST", "/api/auth/sign-out"),
    ];

    /// <summary>Reviewed: cookies whose value is a fresh random credential by design. Their
    /// attributes are compared; their value only by its shape (length and characters).</summary>
    public static readonly IReadOnlyDictionary<string, string> CredentialCookies = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["erp_session"] = "the session token, random for every sign-in (Erp.Kernel.Security.SessionAuthenticationDefaults)",
        ["erp_device"] = "the trusted-device mark, signed and random per device (Erp.Modules.Identity.Auth.TrustedDevices)",
    };

    /// <summary>Reviewed: answer values (by slot, see <see cref="Slots"/>, without the request index)
    /// that differ at random on every answer by design. Empty: every value of the shell's answers is
    /// judged.</summary>
    public static readonly IReadOnlyDictionary<string, string> RandomByDesign = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Headers of the transport, different on every answer by design or the same for every
    /// tenant by design (the body is compared leaf by leaf, its length with it).</summary>
    private static readonly HashSet<string> TransportHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Date", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Server",
    };

    /// <summary>The pace of one tenant's own turns; Y's turns end a moment before X's fourth (see
    /// <see cref="JudgeAsync"/>). Four seconds keep a time sent as an HTTP date (a second's
    /// resolution) of Y's last turn apart from one of X's previous turn.</summary>
    public static readonly TimeSpan Pace = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MinimumLead = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan OtherPause = TimeSpan.FromMilliseconds(80);
    private const int OtherTurns = 3;

    /// <summary>The preferences write's documented values, every combination; the last is Arabic
    /// with Arabic-Indic digits, so the sign-in turn runs in Arabic.</summary>
    public static readonly (string Language, string Numerals)[] PreferenceVariants = [("en", "latn"), ("en", "arab"), ("ar", "latn"), ("ar", "arab")];

    public sealed class Result
    {
        public List<string> Findings { get; } = [];
        public List<string> Unjudged { get; } = [];
        public int Requests { get; set; }
        public int Comparisons { get; set; }
        public int HeaderValuesCompared { get; set; }
        public int CookieValuesCompared { get; set; }
        public int CredentialCookiesShaped { get; set; }
        public int NumbersJudged { get; set; }
        public int TimesJudged { get; set; }
        public HashSet<string> EndpointsJudged { get; } = new(StringComparer.Ordinal);

        /// <summary>The tenants' seed markers (random per test process, in any letter case): replaced
        /// by a placeholder before a value is split, so the counts of numbers judged do not depend on their digits.</summary>
        internal List<string> Canaries { get; } = [];
    }

    /// <summary>One answer: its slots (compared) and the session token it set (sent on, never compared).</summary>
    private sealed record Answer(string Request, DateTimeOffset Start, DateTimeOffset End, IReadOnlyDictionary<string, string> Slots, string? Token);

    private sealed class Actor(string label, SeedTenant tenant, string email)
    {
        public string Label { get; } = label;
        public SeedTenant Tenant { get; } = tenant;
        public string Email { get; } = email;
        public string? Session { get; set; }
        public List<(DateTimeOffset Start, DateTimeOffset End)> Windows { get; } = [];
    }

    /// <summary>A turn: the requests the shell makes for one user action, in order.</summary>
    private sealed record Turn(string Name, Func<HttpClient, Actor, Task<List<Answer>>> Run);

    /// <summary>Runs the check over <paramref name="env"/>'s app host. <paramref name="variants"/>
    /// limits the preferences variants (a self-test needs only the Arabic one).</summary>
    public static async Task<Result> RunAsync(ErpTestEnvironment env, IReadOnlyList<(string Language, string Numerals)>? variants = null)
    {
        var result = new Result();
        result.Canaries.AddRange(env.Plan.Tenants.Select(t => t.Canary).OfType<string>().Where(c => c.Length > 0));
        using var client = env.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        var a = new Actor("tenant A", env.TenantA, env.Email(env.TenantA, "admin"));
        var b = new Actor("tenant B", env.TenantB, env.Email(env.TenantB, "admin"));
        foreach (var actor in new[] { a, b })
        {
            actor.Session = (await SignInAsync(client, actor, result)).Token;
        }
        var turns = new List<Turn>();
        foreach (var (language, numerals) in variants ?? PreferenceVariants)
        {
            turns.Add(new Turn($"preferences (language=\"{language}\", numerals=\"{numerals}\")", async (c, actor) =>
            [
                await SendAsync(c, actor, "PUT", ArabicSession.PreferencesPath, new { language, numerals }, actor.Session, result),
                await SendAsync(c, actor, "GET", "/api/auth/session", null, actor.Session, result),
            ]));
        }
        turns.Add(new Turn("sign-in, session, sign-out", async (c, actor) =>
        {
            var (signIn, token) = await SignInAsync(c, actor, result);
            return
            [
                signIn,
                await SendAsync(c, actor, "GET", "/api/auth/session", null, token, result),
                await SendAsync(c, actor, "POST", "/api/auth/sign-out", null, token, result),
            ];
        }));

        foreach (var turn in turns)
        {
            foreach (var (x, y) in new[] { (a, b), (b, a) })
            {
                await JudgeAsync(client, turn, x, y, result);
            }
        }
        return result;
    }

    private static async Task JudgeAsync(HttpClient client, Turn turn, Actor x, Actor y, Result result)
    {
        x.Windows.Clear();
        y.Windows.Clear();
        // Warm-up: X's first turn follows whatever came before (the other direction's run).
        await turn.Run(client, x);
        var start = DateTimeOffset.UtcNow + Pace;
        var alone = new List<List<Answer>>();
        // How late a turn started against its schedule (a busy machine): added to the tolerance of
        // a time's distance, as it moves "X's previous turn" by as much.
        var late = TimeSpan.Zero;
        for (var i = 0; i < 3; i++)
        {
            await WaitUntilAsync(start + Pace * i);
            alone.Add(await turn.Run(client, x));
            late = Max(late, alone[^1][0].Start - (start + Pace * i));
        }
        // Y's turns end just before X's fourth: they start as long before it as X's own turns took,
        // so a time of Y's last request is a moment back, far from X's previous turn.
        var turnTaken = alone.Max(t => t[^1].End - t[0].Start);
        var lead = Max(MinimumLead, (turnTaken + OtherPause) * OtherTurns + OtherPause);
        var fourth = start + Pace * 3;
        await WaitUntilAsync(fourth - lead);
        for (var i = 0; i < OtherTurns; i++)
        {
            await turn.Run(client, y);
            await Task.Delay(OtherPause);
        }
        await WaitUntilAsync(fourth);
        var after = await turn.Run(client, x);
        late = Max(late, after[0].Start - fourth);

        for (var index = 0; index < after.Count; index++)
        {
            var label = $"{x.Label} {turn.Name}: {after[index].Request}";
            result.EndpointsJudged.Add(after[index].Request.Split(' ', 3) is [var method, var path, ..] ? $"{method} {path.Split('?')[0]}" : after[index].Request);
            var history = alone.Select(t => t[index]).ToList();
            JudgeAnswer(label, history, after[index], x, y, late, result);
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static void JudgeAnswer(string label, List<Answer> alone, Answer after, Actor x, Actor y, TimeSpan late, Result result)
    {
        var keys = alone.SelectMany(a => a.Slots.Keys).Concat(after.Slots.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            result.Comparisons++;
            if (key.StartsWith("header:", StringComparison.Ordinal))
            {
                result.HeaderValuesCompared++;
            }
            if (key.StartsWith("cookie:", StringComparison.Ordinal) && !key.EndsWith(":attributes", StringComparison.Ordinal))
            {
                if (key.EndsWith(":credential", StringComparison.Ordinal))
                {
                    result.CredentialCookiesShaped++;
                }
                else
                {
                    result.CookieValuesCompared++;
                }
            }
            var last = alone[^1].Slots.GetValueOrDefault(key);
            var now = after.Slots.GetValueOrDefault(key);
            if ((last is null) != (now is null))
            {
                result.Findings.Add($"{label} {key}: {(now is null ? $"'{Short(last!)}' alone, missing" : $"missing alone, '{Short(now)}'")} after {y.Label}'s turns");
                continue;
            }
            if (now is null)
            {
                continue;
            }
            var history = alone.Where(a => a.Slots.ContainsKey(key)).ToList();
            var parts = history.Select(a => Split(Unmark(a.Slots[key], result.Canaries))).ToList();
            var current = Split(Unmark(now, result.Canaries));
            var random = RandomByDesign.ContainsKey(key);
            if (parts.Select(p => p.Text).Distinct(StringComparer.Ordinal).Count() > 1 || parts.Any(p => p.Numbers.Count != current.Numbers.Count || p.Times.Count != current.Times.Count))
            {
                if (random)
                {
                    result.Unjudged.Add($"{label} {key} (reviewed: {RandomByDesign[key]})");
                }
                else
                {
                    result.Findings.Add($"{label} {key}: changes along {x.Label}'s own turns in a way this check cannot judge ({string.Join(" | ", history.Select(h => $"'{Short(h.Slots[key])}'"))}); judge it or review it as random by design");
                }
                continue;
            }
            if (!string.Equals(parts[^1].Text, current.Text, StringComparison.Ordinal))
            {
                result.Findings.Add($"{label} {key}: '{Short(history[^1].Slots[key])}' alone, '{Short(now)}' after {y.Label}'s turns");
                continue;
            }
            for (var n = 0; n < current.Numbers.Count; n++)
            {
                result.NumbersJudged++;
                var values = parts.Select(p => p.Numbers[n]).ToList();
                var steps = values.Zip(values.Skip(1), (p, q) => q - p).Distinct().ToList();
                if (steps.Count > 1)
                {
                    if (!random)
                    {
                        result.Findings.Add($"{label} {key}: number {n + 1} moves by uneven steps along {x.Label}'s own turns ({string.Join(", ", values)}); judge it or review it as random by design");
                    }
                    continue;
                }
                var step = steps.Count == 0 ? 0 : steps[0];
                if (current.Numbers[n] - values[^1] != step)
                {
                    result.Findings.Add($"{label} {key}: number {n + 1} moves by {step} from one of {x.Label}'s turns to the next alone ({string.Join(", ", values)}), by {current.Numbers[n] - values[^1]} across {y.Label}'s {OtherTurns} turns ({current.Numbers[n]}): '{Short(now)}'");
                }
            }
            for (var t = 0; t < current.Times.Count; t++)
            {
                result.TimesJudged++;
                var (at, resolution) = current.Times[t];
                var times = parts.Select(p => p.Times[t].At).ToList();
                if (times.Distinct().Count() == 1)
                {
                    if (at != times[0])
                    {
                        result.Findings.Add($"{label} {key}: time {t + 1} is fixed alone ({times[0]:O}), {at:O} after {y.Label}'s turns");
                    }
                    continue;
                }
                // Distance from the start of X's own request, alone and after Y's turns.
                var offsets = history.Select((h, i) => (times[i] - h.Start).TotalMilliseconds).ToList();
                var offset = (at - after.Start).TotalMilliseconds;
                var longest = history.Append(after).Max(h => (h.End - h.Start).TotalMilliseconds);
                var tolerance = 250 + Math.Max(resolution.TotalMilliseconds, offsets.Max() - offsets.Min()) + longest + late.TotalMilliseconds;
                if (offset < offsets.Min() - tolerance || offset > offsets.Max() + tolerance)
                {
                    result.Findings.Add($"{label} {key}: time {t + 1} is {Ms(offsets.Min())} to {Ms(offsets.Max())} from {x.Label}'s request alone, {Ms(offset)} after {y.Label}'s turns ({at:O})");
                }
                // A time inside the other tenant's requests and outside X's own.
                if (resolution < TimeSpan.FromSeconds(1) && Inside(at, y.Windows) && !Inside(at, x.Windows))
                {
                    result.Findings.Add($"{label} {key}: time {t + 1} ({at:O}) falls inside a request of {y.Label}'s and no request of {x.Label}'s");
                }
            }
        }
    }

    private static string Unmark(string value, List<string> canaries) =>
        canaries.Aggregate(value, (text, canary) => text.Replace(canary, "<canary>", StringComparison.OrdinalIgnoreCase));

    private static bool Inside(DateTimeOffset at, List<(DateTimeOffset Start, DateTimeOffset End)> windows) =>
        windows.Any(w => at >= w.Start.AddMilliseconds(-2) && at <= w.End.AddMilliseconds(2));

    private static string Ms(double milliseconds) => $"{milliseconds.ToString("+0;-0", CultureInfo.InvariantCulture)} ms";

    private static async Task WaitUntilAsync(DateTimeOffset at)
    {
        var left = at - DateTimeOffset.UtcNow;
        if (left > TimeSpan.Zero)
        {
            await Task.Delay(left);
        }
    }

    private static async Task<(Answer Answer, string Token)> SignInAsync(HttpClient client, Actor actor, Result result)
    {
        var answer = await SendAsync(client, actor, "POST", "/api/auth/sign-in", new { email = actor.Email, password = ErpTestEnvironment.Password }, null, result,
            (response, slots) =>
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new InvalidOperationException($"Sign-in as {actor.Email} failed: {(int)response.StatusCode}");
                }
            });
        return (answer, answer.Token ?? throw new InvalidOperationException("the sign-in set no session cookie"));
    }

    private static async Task<Answer> SendAsync(HttpClient client, Actor actor, string method, string path, object? body, string? session, Result result,
        Action<HttpResponseMessage, Dictionary<string, string>>? check = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add("X-Erp-Request", "1");
        if (session is not null)
        {
            request.Headers.Add("Cookie", $"erp_session={session}");
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        var start = DateTimeOffset.UtcNow;
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var end = DateTimeOffset.UtcNow;
        result.Requests++;
        actor.Windows.Add((start, end));
        var slots = Slots(response, text, out var token);
        check?.Invoke(response, slots);
        return new Answer($"{method} {path}", start, end, slots, token);
    }

    /// <summary>
    /// Every part of an answer, by slot: <c>status</c>; <c>header:name</c> for every header but the
    /// transport's; <c>cookie:name</c> (its value) and <c>cookie:name:attributes</c> for every cookie
    /// set, a credential cookie's value as <c>cookie:name:credential</c> (its shape only); and
    /// <c>body:path</c> for every leaf of a JSON body (<c>body</c> for any other).
    /// </summary>
    private static Dictionary<string, string> Slots(HttpResponseMessage response, string body, out string? session)
    {
        session = null;
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) };
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (TransportHeaders.Contains(name))
            {
                continue;
            }
            if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var cookie in values)
                {
                    var semicolon = cookie.IndexOf(';', StringComparison.Ordinal);
                    var pair = semicolon < 0 ? cookie : cookie[..semicolon];
                    var attributes = semicolon < 0 ? "" : cookie[(semicolon + 1)..].Trim();
                    var equals = pair.IndexOf('=', StringComparison.Ordinal);
                    var cookieName = equals < 0 ? pair.Trim() : pair[..equals].Trim();
                    var value = equals < 0 ? "" : pair[(equals + 1)..].Trim();
                    if (CredentialCookies.ContainsKey(cookieName))
                    {
                        slots[$"cookie:{cookieName}:credential"] = Shape(value);
                        if (cookieName == "erp_session" && value.Length > 0)
                        {
                            session = value;
                        }
                    }
                    else
                    {
                        slots[$"cookie:{cookieName}"] = value;
                    }
                    slots[$"cookie:{cookieName}:attributes"] = attributes;
                }
                continue;
            }
            slots[$"header:{name.ToLowerInvariant()}"] = string.Join(", ", values);
        }
        if (body.Length == 0)
        {
            return slots;
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            Leaves(document.RootElement, "body", slots);
        }
        catch (JsonException)
        {
            slots["body"] = body;
        }
        return slots;
    }

    /// <summary>A credential's shape: its length and its alphabet (a random token's characters are
    /// any of its alphabet's, so which ones occur is not compared).</summary>
    private static string Shape(string value) =>
        value.Length == 0 ? "<empty>" : $"<{value.Length} characters of {(value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? "base64url" : value.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or '.' or '-' or '_') ? "base64 and dots" : "other characters")}>";

    private static void Leaves(JsonElement element, string path, Dictionary<string, string> slots)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var any = false;
                foreach (var property in element.EnumerateObject())
                {
                    any = true;
                    Leaves(property.Value, $"{path}.{property.Name}", slots);
                }
                if (!any)
                {
                    slots[path] = "{}";
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Leaves(item, $"{path}[{index++}]", slots);
                }
                slots[$"{path}.length"] = index.ToString(CultureInfo.InvariantCulture);
                break;
            default:
                slots[path] = element.GetRawText();
                break;
        }
    }

    /// <summary>A value split into its text (times and numbers replaced by placeholders), its numbers
    /// (Western or Arabic-Indic digits) and its times (ISO 8601 or HTTP dates, with their
    /// resolution).</summary>
    private sealed record Parts(string Text, List<long> Numbers, List<(DateTimeOffset At, TimeSpan Resolution)> Times);

    private static Parts Split(string value)
    {
        var times = new List<(DateTimeOffset, TimeSpan)>();
        var text = IsoTimeRegex().Replace(value, m =>
        {
            var raw = m.Value;
            var zoned = m.Groups["zone"].Success ? raw : raw + "Z";
            if (!DateTimeOffset.TryParse(zoned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                return raw;
            }
            times.Add((at, m.Groups["fraction"].Success ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromSeconds(1)));
            return "<time>";
        });
        text = HttpDateRegex().Replace(text, m =>
        {
            if (!DateTimeOffset.TryParseExact(m.Value, "r", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                return m.Value;
            }
            times.Add((at, TimeSpan.FromSeconds(1)));
            return "<time>";
        });
        // Identifiers stay in the text, compared whole, with their digits written as letters so no
        // count of numbers judged depends on the digits of a random id.
        text = GuidRegex().Replace(text, m => "<id:" + new string(m.Value.Select(c => char.IsAsciiDigit(c) ? (char)('g' + (c - '0')) : c).ToArray()) + ">");
        var numbers = new List<long>();
        text = NumberRegex().Replace(text, m =>
        {
            numbers.Add(long.Parse(Western(m.Value), CultureInfo.InvariantCulture));
            return "<n>";
        });
        return new Parts(text, numbers, times);
    }

    private static string Western(string digits) =>
        new(digits.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c is >= '۰' and <= '۹' ? (char)('0' + (c - '۰')) : c).ToArray());

    private static string Short(string value) => value.Length <= 160 ? value : value[..160] + "…";

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(:\d{2}(?<fraction>\.\d+)?)?(?<zone>Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex IsoTimeRegex();

    [GeneratedRegex(@"(Mon|Tue|Wed|Thu|Fri|Sat|Sun), \d{2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4} \d{2}:\d{2}:\d{2} GMT")]
    private static partial Regex HttpDateRegex();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidRegex();

    /// <summary>A run of digits that fits a long (a longer run stays in the text).</summary>
    [GeneratedRegex(@"(?<![0-9٠-٩۰-۹])[0-9٠-٩۰-۹]{1,18}(?![0-9٠-٩۰-۹])")]
    private static partial Regex NumberRegex();
}
