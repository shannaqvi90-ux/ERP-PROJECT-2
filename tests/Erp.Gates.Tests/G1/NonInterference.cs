using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Erp.Gates.Tests.Infrastructure;
using Erp.Kernel.Lists;
using Erp.Kernel.Modules;
using Erp.Kernel.Security;
using Erp.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Gates.Tests.G1;

/// <summary>
/// G1 non-interference: what one tenant is answered may not depend on what the other tenant did.
/// The marker-based attack only sees a leak that carries something of tenant B's (an id, a text
/// value, a canary). State shared across requests can leak without one: a count cached by search
/// text hands tenant A tenant B's total (critic p05 round 1, plant L3), a variable captured by an
/// endpoint lambda hands on the previous caller's number, a per-id cache hands on a status code.
/// This check needs no marker. For every GET the app exposes, with route values, query parameters,
/// list groupings and searches drawn from both tenants, it compares two answers to the same
/// request from the same signed-in user over the same database:
/// <list type="bullet">
/// <item>in the shared process, sent right after the other tenant sent exactly the same request
/// (so anything kept per request, per id, per search or per caller holds the other tenant's
/// answer at that moment), and</item>
/// <item>in a fresh app process that only the judged tenant has ever used (its own singletons,
/// caches and endpoint closures, untouched by the other tenant).</item>
/// </list>
/// Both answers (status, body and response headers, with trace ids, the credential cookies' values
/// and the transport's own headers left out, and every time replaced by whose request it falls in:
/// <see cref="RequestClock"/>) must be identical. A difference is confirmed once more
/// before it is reported. A request whose answer changes from one call to the next in the fresh
/// process (a counter, a stamp) is judged by how it changes instead: in the shared process, the
/// change from one answer to the next when the judged tenant sends it on its own must equal the
/// change when the other tenant sent the same request in between (a count of every tenant's
/// requests goes up by one alone and by two with the other tenant: critic p04 round 8, plant L3,
/// a process-wide count in a response header). Only an answer that changes the same way each time
/// it is sent alone and still differs is called unstable. The process configuration of every host
/// is compared before and after each judged request: a value written there while serving is
/// process-wide state (<see cref="Erp.Kernel.Hosting.ReadOnlyConfiguration"/>). Both directions
/// run (judging tenant A after tenant B, and tenant B after tenant A), each with its own fresh
/// process. The check is blind unless enough requests have different true answers in the two
/// tenants, so that count has a ratchet minimum. Static fields are shared by the two hosts (they
/// run in one test process) and are covered by the process-state gate instead.
/// </summary>
public static partial class NonInterference
{
    public sealed record Result(
        IReadOnlyList<string> Findings,
        IReadOnlyList<string> Unstable,
        IReadOnlyList<string> BlindSpots,
        int Requests,
        int Comparisons,
        int Discriminating,
        int Endpoints,
        int WriteComparisons = 0,
        int WriteEndpoints = 0,
        int WriteVariants = 0,
        int ArabicComparisons = 0,
        int ArabicAnswers = 0,
        int ArabicWriteComparisons = 0,
        int ArabicWorkspaceWriteComparisons = 0)
    {
        /// <summary>Response header lines compared (in the judged tenants' true answers): a check
        /// that compares no headers is blind to a leak sent in one (critic p04 round 8).</summary>
        public int HeadersCompared { get; init; }

        /// <summary>Answers that changed from one call to the next in the fresh process and were
        /// judged by how they change.</summary>
        public int ChangeProbes { get; init; }

        /// <summary>Configuration keys watched for writes in the hosts compared.</summary>
        public int ConfigurationKeysWatched { get; init; }

        /// <summary>Times in compared answers attributed to the request answered, to an earlier
        /// request of the same tenant or to the other tenant's (critic p04 round 9: times used to
        /// be blanked).</summary>
        public int TimesAttributed { get; init; }

        /// <summary>Times whose interval touched windows of more than one kind; each made its
        /// comparison run again with the requests paced apart.</summary>
        public int TimesAmbiguous { get; init; }

        /// <summary>Comparisons run again, paced, for an ambiguous time.</summary>
        public int PacedRuns { get; init; }

        /// <summary>Values of cookies other than the credential cookies, compared as sent (critic
        /// p04 round 9: every cookie value used to be blanked).</summary>
        public int CookieValuesCompared { get; init; }

        /// <summary>Write comparisons of partial bodies (<see cref="TenantActivity.PartialVariantsOf"/>).</summary>
        public int PartialWriteComparisons { get; init; }

        /// <summary>Partial bodies leaving out an enumerated field, judged against the other
        /// tenant's choice of that field (<see cref="JudgePartialAfterOthersChoiceAsync"/>).</summary>
        public int PartialChoiceChecks { get; init; }
    }

    private static readonly string[] SharedTexts = ["a", "al", "e", "1", "co", "ad"];

    /// <param name="only">The endpoints compared and written (default: every endpoint, which is what
    /// the gate runs); the gate's self-test passes the planted module's surface, every comparison
    /// unchanged (see <see cref="IsolationAttack.RunAsync"/>).</param>
    public static async Task<Result> RunAsync(ErpTestEnvironment env, Func<ApiEndpoint, bool>? only = null)
    {
        var a = env.TenantA;
        var b = env.TenantB;
        using var anonymous = env.CreateClient();
        var openApi = await OpenApiDocument.LoadAsync(anonymous);
        var catalog = env.Factory.Services.GetRequiredService<ModuleCatalog>();
        var endpoints = EndpointInventory.From(env.Factory.Services)
            .Where(e => e.Method == "GET" && e.Surface == SurfaceKind.Data && e.Pattern.StartsWith("/api/", StringComparison.Ordinal) && (only?.Invoke(e) ?? true))
            .ToList();

        // Both tenants first make every write the app offers on their own records in the shared
        // process, so whatever a write leaves behind (a counter, a "recently changed" list, a
        // cache filled on save) is there when the other tenant reads.
        var allEndpoints = EndpointInventory.From(env.Factory.Services).Where(e => only?.Invoke(e) ?? true).ToList();
        var state = new State();
        state.Clock.Tolerance = await DatabaseClockToleranceAsync(env);
        state.Configuration.Watch("the shared process", env.Factory.Services);
        var writes = 0;
        var partialAccepted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tenant in new[] { b, a })
        {
            var writer = await TenantActivity.StartAsync(env, tenant, allEndpoints, openApi);
            writer.Written = (endpoint, variant) => state.Configuration.Check(state,
                $"{endpoint.Method} {endpoint.Pattern}{(variant is null ? "" : $" ({variant})")} (each tenant's own writes before the comparison)");
            var started = DateTimeOffset.UtcNow;
            try
            {
                await writer.WriteAsync(await TenantSnapshot.TakeAsync(env, tenant.Id, tenant.Canary, tenant.Code));
                writes += writer.SuccessfulWrites;
                partialAccepted.UnionWith(writer.PartialAccepted);
            }
            finally
            {
                writer.Dispose();
                // Times the tenant's own writing stamped are its own, whenever they show up.
                state.Clock.Add(started, DateTimeOffset.UtcNow, tenant == a ? 'A' : 'B');
            }
        }
        state.Configuration.Check(state, "both tenants' writes before the comparison (between writes)");

        var ownA = await TenantSnapshot.TakeAsync(env, a.Id, a.Canary, a.Code);
        var ownB = await TenantSnapshot.TakeAsync(env, b.Id, b.Canary, b.Code);
        var textsA = (await VictimValues.ReadAsync(env, ownA, b.Id)).Strings;
        var textsB = (await VictimValues.ReadAsync(env, ownB, a.Id)).Strings;

        using var sharedA = await env.SignInWithTokenAsync(env.Email(a, "admin"));
        using var sharedB = await env.SignInWithTokenAsync(env.Email(b, "admin"));
        await using var freshProcessA = env.StartFreshProcess();
        await using var freshProcessB = env.StartFreshProcess();
        using var freshA = FreshClient(freshProcessA, sharedA);
        state.Configuration.Watch("tenant A's fresh process", freshProcessA.Services);
        state.Configuration.Watch("tenant B's fresh process", freshProcessB.Services);
        using var freshB = FreshClient(freshProcessB, sharedB);

        var uris = RequestsFor(endpoints, openApi, catalog, ownA, ownB, textsA, textsB, a.Code, b.Code);
        var englishA = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (endpoint, uri) in uris)
        {
            var judgedA = await JudgeAsync(state, uri, "tenant A", sharedA, freshA, "tenant B", sharedB);
            var judgedB = await JudgeAsync(state, uri, "tenant B", sharedB, freshB, "tenant A", sharedA);
            englishA[uri] = judgedA;
            state.Endpoints.Add(endpoint.Key);
            if (judgedA is { } answerA && judgedB is { } answerB && answerA != answerB)
            {
                state.Discriminating++;
            }
        }

        // The Arabic side (critic p04 round 4): the request language is the signed-in user's, so
        // the comparisons above only ever ran the English branch of every handler. Every request
        // once more from each tenant's Arabic administrator (Arabic, Arabic-Indic digits), in the
        // shared process right after the other tenant's Arabic administrator sent it, against a
        // fresh process only the judged tenant has used. Answers in Arabic (more Arabic letters
        // than the English administrator's answer to the same request) prove the Arabic branch ran.
        var blind = new List<string>();
        using var arabicA = await ArabicSession.SignInWithTokenAsync(env, a);
        using var arabicB = await ArabicSession.SignInWithTokenAsync(env, b);
        using var freshArabicA = FreshClient(freshProcessA, arabicA);
        using var freshArabicB = FreshClient(freshProcessB, arabicB);
        foreach (var (endpoint, uri) in uris)
        {
            var before = state.Comparisons;
            var judgedA = await JudgeAsync(state, uri, "tenant A in Arabic", arabicA, freshArabicA, "tenant B in Arabic", arabicB);
            await JudgeAsync(state, uri, "tenant B in Arabic", arabicB, freshArabicB, "tenant A in Arabic", arabicA);
            state.ArabicComparisons += state.Comparisons - before;
            if (judgedA is { } arabic && englishA.GetValueOrDefault(uri) is { } english && ArabicSession.ArabicLetters(arabic) > ArabicSession.ArabicLetters(english))
            {
                state.ArabicAnswers++;
            }
        }

        // Writes: what a write answers may not depend on the other tenant's same write either,
        // for every documented value of every enumerated field of its body, and in Arabic.
        await CompareWritesAsync(env, state, allEndpoints, openApi, sharedA, sharedB, freshA, freshB, arabicA, arabicB, freshArabicA, freshArabicB, freshProcessA, freshProcessB, partialAccepted);
        blind.AddRange(state.WorkspaceWideFailures);

        foreach (var (name, client) in new[] { ("tenant A's Arabic administrator", arabicA), ("tenant B's Arabic administrator", arabicB),
                     ("tenant A's Arabic administrator (fresh process)", freshArabicA), ("tenant B's Arabic administrator (fresh process)", freshArabicB) })
        {
            if (!await ArabicSession.IsArabicAsync(client))
            {
                blind.Add($"{name} no longer answers in Arabic with Arabic-Indic digits");
            }
        }
        if (state.ArabicComparisons == 0)
        {
            blind.Add("no request was compared in Arabic");
        }
        if (state.ArabicAnswers == 0)
        {
            blind.Add("no answer to an Arabic session was more Arabic than the English one, so the Arabic branch may never have run");
        }
        if (state.ArabicWriteComparisons == 0)
        {
            blind.Add("no write was compared in Arabic");
        }
        if (state.WriteComparisons == 0)
        {
            blind.Add("no write was compared");
        }
        else if (state.UnstableWrites * 4 > state.WriteComparisons)
        {
            blind.Add($"{state.UnstableWrites} of {state.WriteComparisons} writes answered differently from one call to the next in a fresh process");
        }
        if (state.Comparisons == 0)
        {
            blind.Add("no request was compared");
        }
        else if ((state.Unstable.Count - state.UnstableWrites) * 4 > state.Comparisons)
        {
            blind.Add($"{state.Unstable.Count - state.UnstableWrites} of {state.Comparisons} requests answered differently from one call to the next in a fresh process");
        }
        if (writes == 0)
        {
            blind.Add("neither tenant made a successful write before the comparison");
        }
        if (state.Discriminating == 0)
        {
            blind.Add("no request had different true answers in the two tenants, so no interference could show");
        }
        if (state.HeadersCompared == 0)
        {
            blind.Add("no response header was compared");
        }
        if (state.Configuration.KeysWatched == 0)
        {
            blind.Add("no configuration key was watched");
        }
        return new Result(state.Findings.Distinct(StringComparer.Ordinal).ToList(), state.Unstable, blind, state.Requests, state.Comparisons, state.Discriminating, state.Endpoints.Count,
            state.WriteComparisons, state.WriteEndpoints.Count, state.WriteVariants.Count, state.ArabicComparisons, state.ArabicAnswers, state.ArabicWriteComparisons,
            state.ArabicWorkspaceWriteComparisons)
        {
            HeadersCompared = state.HeadersCompared,
            ChangeProbes = state.ChangeProbes,
            ConfigurationKeysWatched = state.Configuration.KeysWatched,
            TimesAttributed = state.Clock.TimesAttributed,
            TimesAmbiguous = state.Clock.TimesAmbiguous,
            PacedRuns = state.Clock.PacedRuns,
            CookieValuesCompared = state.Clock.CookieValuesCompared,
            PartialWriteComparisons = state.PartialWriteComparisons,
            PartialChoiceChecks = state.PartialChoiceChecks,
        };
    }

    /// <summary>
    /// Every write that acts on an existing record (PUT and PATCH, and POST on a record's route:
    /// creates and deletes answer a new record each time), with its default body and with every
    /// variant (<see cref="TenantActivity.VariantsOf"/>: each documented value of each enumerated
    /// field, so the Arabic side of a language or digits field is judged as well as the English
    /// side), compared as for reads: the judged tenant's answer in the shared process right after
    /// the other tenant made the same write with the same value, against its answer in a fresh
    /// process only it has used, written twice there so a difference between two such writes
    /// counts as unstable rather than as a finding. Both directions. Versions (a row's
    /// concurrency token, which every save changes) are left out of the comparison along with
    /// times and trace ids. The partial bodies the product accepts (<see cref="TenantActivity.PartialVariantsOf"/>,
    /// critic p04 round 11) are compared the same way, and those that leave out an enumerated field
    /// are also judged against the other tenant's choice of it (<see cref="JudgePartialAfterOthersChoiceAsync"/>).
    /// </summary>
    private static async Task CompareWritesAsync(ErpTestEnvironment env, State state, IReadOnlyList<ApiEndpoint> allEndpoints, OpenApiDocument openApi,
        HttpClient sharedA, HttpClient sharedB, HttpClient freshA, HttpClient freshB,
        HttpClient arabicA, HttpClient arabicB, HttpClient freshArabicA, HttpClient freshArabicB, ErpAppFactory freshProcessA, ErpAppFactory freshProcessB,
        IReadOnlySet<string> partialAccepted)
    {
        var a = env.TenantA;
        var b = env.TenantB;
        var activityA = await TenantActivity.StartAsync(env, a, allEndpoints, openApi);
        var activityB = await TenantActivity.StartAsync(env, b, allEndpoints, openApi);
        // The Arabic administrators work in one branch of a company only (gate seed), so a write of
        // a record every company shares is refused to both alike, and comparing two refusals never
        // runs the Arabic branch of its success path. Such a write is compared once more between
        // the two tenants' workspace-wide Arabic administrators (p04 round 7), created when needed.
        var wideClients = new List<HttpClient>();
        WriteSide[]? wideSides = null;
        async Task<WriteSide[]> WideSidesAsync(TenantSnapshot ownA, TenantSnapshot ownB)
        {
            if (wideSides is not null) return wideSides;
            var emailA = await activityA.ArabicWorkspaceEmailAsync();
            var emailB = await activityB.ArabicWorkspaceEmailAsync();
            var wideA = await ArabicSession.SignInWorkspaceWideAsync(env, emailA, bearer: true);
            wideClients.Add(wideA);
            var wideB = await ArabicSession.SignInWorkspaceWideAsync(env, emailB, bearer: true);
            wideClients.Add(wideB);
            var freshWideA = FreshClient(freshProcessA, wideA);
            wideClients.Add(freshWideA);
            var freshWideB = FreshClient(freshProcessB, wideB);
            wideClients.Add(freshWideB);
            return wideSides =
            [
                new WriteSide("tenant A's workspace-wide administrator in Arabic", activityA, ownA, wideA, freshWideA, emailA.Split('@')[0]),
                new WriteSide("tenant B's workspace-wide administrator in Arabic", activityB, ownB, wideB, freshWideB, emailB.Split('@')[0]),
            ];
        }
        try
        {
            var ownA = await TenantSnapshot.TakeAsync(env, a.Id, a.Canary, a.Code);
            var ownB = await TenantSnapshot.TakeAsync(env, b.Id, b.Canary, b.Code);
            var sides = new[]
            {
                new WriteSide("tenant A", activityA, ownA, sharedA, freshA),
                new WriteSide("tenant B", activityB, ownB, sharedB, freshB),
            };
            var arabicSides = new[]
            {
                new WriteSide("tenant A in Arabic", activityA, ownA, arabicA, freshArabicA, ArabicSession.Local),
                new WriteSide("tenant B in Arabic", activityB, ownB, arabicB, freshArabicB, ArabicSession.Local),
            };
            foreach (var endpoint in activityA.Writes.Where(ComparableWrite))
            {
                // Every variant first, then every partial body (critic p04 round 11), every enumerated
                // field at its first value last (it puts both tenants' records back; without
                // enumerated fields, the default body).
                // The partial bodies the product accepted in either tenant's own writes above (one it
                // refuses never reaches the handler's partial path).
                var partials = activityA.PartialVariantsOf(endpoint).Where(v => partialAccepted.Contains($"{endpoint.Key}|{v.Label}")).ToList();
                foreach (var variant in activityA.VariantsOf(endpoint).Concat(partials).Append(activityA.FirstValues(endpoint)))
                {
                    foreach (var (judged, other) in new[] { (sides[0], sides[1]), (sides[1], sides[0]) })
                    {
                        var before = state.WriteComparisons;
                        await JudgeWriteAsync(state, endpoint, variant, judged, other);
                        if (variant is { Partial: true })
                        {
                            state.PartialWriteComparisons += state.WriteComparisons - before;
                        }
                    }
                    state.WriteEndpoints.Add(endpoint.Key);
                    if (variant is not null)
                    {
                        state.WriteVariants.Add($"{endpoint.Key}|{variant.Label}");
                    }
                }
                // A partial body that leaves out an enumerated field, against the other tenant's
                // choice of that field (critic p04 round 11, plant SL1), both ways.
                foreach (var partial in partials.DistinctBy(v => v.Label))
                {
                    if (OthersChoice(activityA, endpoint, partial) is not { } choice)
                    {
                        continue;
                    }
                    foreach (var (judged, other) in new[] { (sides[0], sides[1]), (sides[1], sides[0]) })
                    {
                        await JudgePartialAfterOthersChoiceAsync(state, endpoint, partial, activityA.FirstValues(endpoint), choice, judged, other);
                    }
                }
                // The same write by both Arabic administrators, every enumerated field at its last
                // documented value (for the shell's own preferences: Arabic with Arabic-Indic
                // digits, which keeps the sessions Arabic).
                var last = activityA.LastValues(endpoint);
                var refusedWholeWorkspace = false;
                foreach (var (judged, other) in new[] { (arabicSides[0], arabicSides[1]), (arabicSides[1], arabicSides[0]) })
                {
                    var before = state.WriteComparisons;
                    var truth = await JudgeWriteAsync(state, endpoint, last, judged, other);
                    state.ArabicWriteComparisons += state.WriteComparisons - before;
                    refusedWholeWorkspace |= IsWorkspaceRefusal(truth);
                }
                if (refusedWholeWorkspace)
                {
                    var wide = await WideSidesAsync(ownA, ownB);
                    foreach (var (judged, other) in new[] { (wide[0], wide[1]), (wide[1], wide[0]) })
                    {
                        var before = state.WriteComparisons;
                        var truth = await JudgeWriteAsync(state, endpoint, last, judged, other);
                        state.ArabicWriteComparisons += state.WriteComparisons - before;
                        state.ArabicWorkspaceWriteComparisons += state.WriteComparisons - before;
                        if (StatusOf(truth) is < 200 or >= 300)
                        {
                            state.WorkspaceWideFailures.Add($"{judged.Name}: {endpoint.Method} {endpoint.Pattern} answered {Short(truth)}, so the success path of a write every company shares was not compared in Arabic");
                        }
                    }
                }
                await ArabicSession.PrepareAsync(arabicA);
                await ArabicSession.PrepareAsync(arabicB);
                await ArabicSession.PrepareAsync(freshArabicA);
                await ArabicSession.PrepareAsync(freshArabicB);
            }
        }
        finally
        {
            wideClients.ForEach(c => c.Dispose());
            activityA.Dispose();
            activityB.Dispose();
        }
    }

    /// <summary>The other tenant's choice against which a partial body is judged: every enumerated
    /// top-level field the partial body leaves out, at its last documented value (null when it
    /// leaves out none with two values or more).</summary>
    private static WriteVariant? OthersChoice(TenantActivity activity, ApiEndpoint endpoint, WriteVariant partial)
    {
        if (activity.FirstValues(endpoint) is not { } first)
        {
            return null;
        }
        var settings = first.Settings.Where(s => s.Leaf.Path.Count == 1 && partial.Omitted.Contains(s.Leaf.Path[0]) && s.Leaf.Values.Count > 1)
            .Select(s => (s.Leaf, s.Leaf.Values[^1])).ToList();
        return settings.Count == 0 ? null : new WriteVariant(string.Join(" ", settings.Select(s => $"{s.Leaf.Name}={s.Item2.ToJsonString()}")), settings, null, null);
    }

    /// <summary>
    /// A partial body against the other tenant's choice (critic p04 round 11, plant SL1: the digits
    /// last chosen by anyone, kept in a file, answered a language-only switch). Both tenants first
    /// write every enumerated field at its first value; the judged tenant sends the partial body
    /// twice (its answer must be stable), then once right after the other tenant wrote the fields
    /// the partial body leaves out at their last documented value. The judged tenant's own requests
    /// are the same each time, so its answer must be too: one that follows the other tenant's choice
    /// back and forth (first value, then the choice again) is a finding. All in the shared process,
    /// without a fresh one, whose baseline state kept outside the process would reach as well.
    /// </summary>
    private static async Task JudgePartialAfterOthersChoiceAsync(State state, ApiEndpoint endpoint, WriteVariant partial, WriteVariant? first, WriteVariant choice,
        WriteSide judged, WriteSide other)
    {
        var label = $"{endpoint.Method} {endpoint.Pattern} ({partial})";
        async Task<string> WriteAsync(WriteSide side, WriteVariant? variant)
        {
            state.Requests++;
            var started = await state.Clock.StartAsync();
            var (status, text, headers) = await side.Activity.WriteThroughWithHeadersAsync(side.Shared, $"{side.Name} (shared process)", endpoint, side.Own,
                $"partial body after the other tenant's choice, {label}", variant, side.Owner);
            var context = state.Clock.End(started, TenantOf(side.Name));
            return Answer(status, NormalizeWrite(text, context), NormalizeHeaders(headers, context));
        }
        state.Configuration.Check(state, $"a request sent between the judged ones (before {label} after the other tenant's choice)");
        try
        {
            await WriteAsync(judged, first);
            await WriteAsync(other, first);
            var before = await WriteAsync(judged, partial);
            var again = await WriteAsync(judged, partial);
            state.PartialChoiceChecks++;
            state.WriteComparisons++;
            if (again != before)
            {
                state.UnstableWrites++;
                state.Unstable.Add($"{judged.Name}: {label} answers differently from one write to the next ({Short(before)} / {Short(again)})");
                return;
            }
            await WriteAsync(other, choice);
            var after = await WriteAsync(judged, partial);
            if (after == before)
            {
                return;
            }
            await WriteAsync(other, first);
            var back = await WriteAsync(judged, partial);
            await WriteAsync(other, choice);
            var forth = await WriteAsync(judged, partial);
            if (back == before && forth == after)
            {
                state.Findings.Add($"{judged.Name}: {label} is answered {Difference(after, before)} right after {other.Name} wrote {choice}, " +
                                   $"but {Difference(before, after)} after {other.Name} wrote {first?.Label ?? "the default body"}; {judged.Name}'s own requests were the same");
            }
            else
            {
                state.UnstableWrites++;
                state.Unstable.Add($"{judged.Name}: {label} changed after {other.Name} wrote {choice} but did not follow it back and forth ({Short(before)} / {Short(after)} / {Short(back)} / {Short(forth)})");
            }
        }
        finally
        {
            await WriteAsync(other, first);
            await WriteAsync(judged, first);
            state.Configuration.Check(state, $"{label} after the other tenant's choice");
        }
    }

    /// <summary>The status of a judged write answer (<c>"{status} {body}"</c>).</summary>
    private static int StatusOf(string answer) => int.TryParse(answer.Split(' ', 2)[0], out var status) ? status : 0;

    /// <summary>Between an answer's status and body and its normalised response headers.</summary>
    internal const string HeadersMark = "\n[headers]\n";

    /// <summary>An answer as compared: status, normalised body, then the normalised headers.</summary>
    internal static string Answer(int status, string normalizedBody, string normalizedHeaders) =>
        $"{status} {normalizedBody}{HeadersMark}{normalizedHeaders}";

    /// <summary>The status and body of a compared answer, without its headers.</summary>
    private static string WithoutHeaders(string answer) =>
        answer.IndexOf(HeadersMark, StringComparison.Ordinal) is var at and >= 0 ? answer[..at] : answer;

    /// <summary>How many header lines a compared answer carries.</summary>
    private static int HeaderLines(string answer) =>
        answer.IndexOf(HeadersMark, StringComparison.Ordinal) is var at and >= 0 && answer.Length > at + HeadersMark.Length
            ? answer[(at + HeadersMark.Length)..].Split('\n').Length
            : 0;

    /// <summary>Whether a judged write answer is a refusal for want of the whole workspace.</summary>
    private static bool IsWorkspaceRefusal(string answer) =>
        ArabicSession.IsWorkspaceRefusal(StatusOf(answer), WithoutHeaders(answer).Split(' ', 2) is [_, var body] ? body : "");

    private sealed record WriteSide(string Name, TenantActivity Activity, TenantSnapshot Own, HttpClient Shared, HttpClient Fresh, string Owner = "admin");

    /// <summary>Writes whose true answer is the same each time the same caller makes them: every
    /// write on an existing record (creates and deletes answer a new record each time).</summary>
    internal static bool ComparableWrite(ApiEndpoint endpoint) =>
        endpoint.Method is "PUT" or "PATCH" || (endpoint.Method == "POST" && endpoint.RouteParameters.Count > 0);

    /// <returns>The judged side's true answer (fresh process), <c>"{status} {body}"</c>.</returns>
    private static async Task<string> JudgeWriteAsync(State state, ApiEndpoint endpoint, WriteVariant? variant, WriteSide judged, WriteSide other)
    {
        var label = $"{endpoint.Method} {endpoint.Pattern}{(variant is null ? "" : $" ({variant})")}";
        async Task<string> WriteAsync(WriteSide side, bool fresh)
        {
            state.Requests++;
            var started = await state.Clock.StartAsync();
            var (status, text, headers) = await side.Activity.WriteThroughWithHeadersAsync(fresh ? side.Fresh : side.Shared, $"{side.Name} ({(fresh ? "fresh process" : "shared process")})",
                endpoint, side.Own, $"write comparison, {label}", variant, side.Owner);
            var context = state.Clock.End(started, TenantOf(side.Name));
            return Answer(status, NormalizeWrite(text, context), NormalizeHeaders(headers, context));
        }

        // Whatever changed the configuration since the last check was sent between judged requests
        // (an Arabic session's own settings, say): named as such, not as this write.
        state.Configuration.Check(state, $"a request sent between the judged ones (before {label})");
        try
        {
            return await PacedWhenAmbiguousAsync(state, () => JudgeWriteCoreAsync(state, label, judged, other, WriteAsync));
        }
        finally
        {
            state.Configuration.Check(state, label);
        }
    }

    private static async Task<string> JudgeWriteCoreAsync(State state, string label, WriteSide judged, WriteSide other, Func<WriteSide, bool, Task<string>> WriteAsync)
    {
        await WriteAsync(judged, true); // the fresh process's state holds the judged tenant's own write
        var truth = await WriteAsync(judged, true);
        await WriteAsync(other, false);
        var answer = await WriteAsync(judged, false);
        state.WriteComparisons++;
        state.HeadersCompared += HeaderLines(truth);
        if (answer == truth)
        {
            return truth;
        }
        var truthAgain = await WriteAsync(judged, true);
        if (truthAgain != truth)
        {
            state.ChangeProbes++;
            if (await ChangeDependsOnOtherAsync(() => WriteAsync(judged, false), () => WriteAsync(other, false)) is { } change)
            {
                state.Findings.Add($"{judged.Name}: {label} {change} {other.Name} made the same write in between");
                return truth;
            }
            state.UnstableWrites++;
            state.Unstable.Add($"{judged.Name}: {label} answers differently from one write to the next ({Short(truth)} / {Short(truthAgain)})");
            return truth;
        }
        await WriteAsync(other, false);
        var answerAgain = await WriteAsync(judged, false);
        if (answerAgain == truth)
        {
            state.UnstableWrites++;
            state.Unstable.Add($"{judged.Name}: {label} differed once in the shared process ({Short(answer)}), then matched");
            return truth;
        }
        state.Findings.Add($"{judged.Name}: {label} is answered {Difference(answerAgain, truth)} in the shared process right after {other.Name} made the same write, " +
                           $"but {Difference(truth, answerAgain)} by a fresh process only {judged.Name} has used");
        return truth;
    }

    /// <summary><see cref="Normalize"/>, and every <c>version</c> field (a row's concurrency
    /// token, which each save changes) left out.</summary>
    internal static string NormalizeWrite(string text, AnswerContext? context = null)
    {
        try
        {
            return Scrub(DropVersions(JsonNode.Parse(text)), context)?.ToJsonString() ?? "";
        }
        catch (JsonException)
        {
            return TimeRegex().Replace(text, m => TimeToken(m, context));
        }
    }

    /// <summary>Whose request a side or a judged label is ('A', 'B', or '?' when unknown).</summary>
    internal static char TenantOf(string label) =>
        label.StartsWith("tenant A", StringComparison.Ordinal) ? 'A' : label.StartsWith("tenant B", StringComparison.Ordinal) ? 'B' : '?';

    /// <summary>
    /// Runs one comparison; when a time in any of its answers was ambiguous (its interval touched
    /// windows of more than one kind, as a time to the whole second does among requests a few
    /// milliseconds apart), what that run found is set aside and the comparison runs once more
    /// with every request paced apart by more than the interval, so the time is attributed.
    /// </summary>
    private static async Task<T> PacedWhenAmbiguousAsync<T>(State state, Func<Task<T>> compare)
    {
        var ambiguous = state.Clock.TimesAmbiguous;
        var findings = state.Findings.Count;
        var unstable = state.Unstable.Count;
        var unstableWrites = state.UnstableWrites;
        state.Clock.WidestAmbiguous = TimeSpan.Zero;
        var result = await compare();
        if (state.Clock.TimesAmbiguous == ambiguous || state.Clock.Pace > TimeSpan.Zero)
        {
            return result;
        }
        state.Findings.RemoveRange(findings, state.Findings.Count - findings);
        state.Unstable.RemoveRange(unstable, state.Unstable.Count - unstable);
        state.UnstableWrites = unstableWrites;
        state.Clock.Pace = state.Clock.WidestAmbiguous + TimeSpan.FromMilliseconds(2);
        state.Clock.PacedRuns++;
        try
        {
            return await compare();
        }
        finally
        {
            state.Clock.Pace = TimeSpan.Zero;
        }
    }

    private static JsonNode? DropVersions(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(k => string.Equals(k, "version", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    obj.Remove(key);
                }
                foreach (var (_, child) in obj.ToList())
                {
                    DropVersions(child);
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    DropVersions(item);
                }
                break;
        }
        return node;
    }

    internal sealed class State
    {
        public List<string> Findings { get; } = [];
        public List<string> Unstable { get; } = [];
        public HashSet<string> Endpoints { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WriteEndpoints { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WriteVariants { get; } = new(StringComparer.Ordinal);
        public int Requests;
        public int Comparisons;
        public int Discriminating;
        public int WriteComparisons;
        public int UnstableWrites;
        public int ArabicComparisons;
        public int ArabicAnswers;
        public int ArabicWriteComparisons;
        public int ArabicWorkspaceWriteComparisons;
        public int HeadersCompared;
        public int ChangeProbes;
        public int PartialWriteComparisons;
        public int PartialChoiceChecks;
        public List<string> WorkspaceWideFailures { get; } = [];
        public ConfigurationWatch Configuration { get; } = new();
        public RequestClock Clock { get; } = new();
    }

    /// <summary>One comparison. Returns the judged tenant's true (fresh-process) answer when it is
    /// stable, otherwise null.</summary>
    private static async Task<string?> JudgeAsync(State state, string uri, string judged, HttpClient shared, HttpClient fresh, string other, HttpClient otherShared)
    {
        state.Configuration.Check(state, $"a request sent between the judged ones (before GET {uri})");
        try
        {
            return await PacedWhenAmbiguousAsync(state, () => JudgeCoreAsync(state, uri, judged, shared, fresh, other, otherShared));
        }
        finally
        {
            state.Configuration.Check(state, $"GET {uri}");
        }
    }

    private static async Task<string?> JudgeCoreAsync(State state, string uri, string judged, HttpClient shared, HttpClient fresh, string other, HttpClient otherShared)
    {
        var judgedTenant = TenantOf(judged);
        var otherTenant = TenantOf(other);
        // An offset page (skip) is a jump into a list the other tenant has just opened: the other
        // tenant sends the same query's first page, then the offset page (critic p05 round 5, plant
        // L10: offset pages reused the total the last first page counted, whatever its tenant).
        var firstPage = FirstPageOf(uri);
        await GetAsync(state, fresh, uri, judgedTenant); // warms whatever the fresh process keeps, with the judged tenant's own answer
        var truth = await GetAsync(state, fresh, uri, judgedTenant);
        if (firstPage is not null)
        {
            await GetAsync(state, otherShared, firstPage, otherTenant);
        }
        await GetAsync(state, otherShared, uri, otherTenant);
        var answer = await GetAsync(state, shared, uri, judgedTenant);
        state.Comparisons++;
        state.HeadersCompared += HeaderLines(truth);
        if (answer == truth)
        {
            return truth;
        }
        var truthAgain = await GetAsync(state, fresh, uri, judgedTenant);
        if (truthAgain != truth)
        {
            state.ChangeProbes++;
            if (await ChangeDependsOnOtherAsync(() => GetAsync(state, shared, uri, judgedTenant), async () =>
                {
                    if (firstPage is not null)
                    {
                        await GetAsync(state, otherShared, firstPage, otherTenant);
                    }
                    await GetAsync(state, otherShared, uri, otherTenant);
                }) is { } change)
            {
                state.Findings.Add($"{judged}: GET {uri} {change} {other} sent the same request in between");
                return null;
            }
            state.Unstable.Add($"{judged}: GET {uri} answers differently from one call to the next ({Short(truth)} / {Short(truthAgain)})");
            return null;
        }
        if (firstPage is not null)
        {
            await GetAsync(state, otherShared, firstPage, otherTenant);
        }
        await GetAsync(state, otherShared, uri, otherTenant);
        var answerAgain = await GetAsync(state, shared, uri, judgedTenant);
        if (answerAgain == truth)
        {
            state.Unstable.Add($"{judged}: GET {uri} differed once in the shared process ({Short(answer)}), then matched");
            return truth;
        }
        state.Findings.Add($"{judged}: GET {uri} is answered {Difference(answerAgain, truth)} in the shared process right after {other} sent the same request, " +
                           $"but {Difference(truth, answerAgain)} by a fresh process only {judged} has used");
        return truth;
    }

    /// <summary>For a request with <c>skip</c>, the same request without it (the query's first
    /// page); otherwise null.</summary>
    internal static string? FirstPageOf(string uri)
    {
        var at = uri.IndexOf('?', StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }
        var parts = uri[(at + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (!parts.Any(p => p.StartsWith("skip=", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        return uri[..at] + "?" + string.Join("&", parts.Where(p => !p.StartsWith("skip=", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<string> GetAsync(State state, HttpClient client, string uri, char tenant)
    {
        state.Requests++;
        var started = await state.Clock.StartAsync();
        using var response = await client.GetAsync(uri);
        var body = await response.Content.ReadAsStringAsync();
        var context = state.Clock.End(started, tenant);
        return Answer((int)response.StatusCode, Normalize(body, context), NormalizeHeaders(response, context));
    }

    private static HttpClient FreshClient(ErpAppFactory process, HttpClient signedIn)
    {
        var client = process.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;
        foreach (var language in signedIn.DefaultRequestHeaders.AcceptLanguage)
        {
            client.DefaultRequestHeaders.AcceptLanguage.Add(language);
        }
        return client;
    }

    /// <summary>Every GET with values from both tenants: route values (each tenant's ids and
    /// code), each documented query parameter alone (texts both tenants hold, each tenant's own
    /// texts and ids, flags and numbers), and for registered lists every grouping and sort, every
    /// sort with searches and with a grouping, and offset (skip) pages of each.</summary>
    internal static List<(ApiEndpoint Endpoint, string Uri)> RequestsFor(IReadOnlyList<ApiEndpoint> endpoints, OpenApiDocument openApi, ModuleCatalog catalog,
        TenantSnapshot ownA, TenantSnapshot ownB, IReadOnlyList<string> textsA, IReadOnlyList<string> textsB, string codeA, string codeB)
    {
        var idsA = ownA.IdsByTable.Values.Select(ids => ids.FirstOrDefault()).Where(id => id != Guid.Empty).Append(ownA.TenantId).Select(i => i.ToString()).ToList();
        var idsB = ownB.IdsByTable.Values.Select(ids => ids.FirstOrDefault()).Where(id => id != Guid.Empty).Append(ownB.TenantId).Select(i => i.ToString()).ToList();
        var texts = SharedTexts.Concat(textsA.Take(2)).Concat(textsB.Take(2)).Distinct(StringComparer.Ordinal).ToList();
        var result = new List<(ApiEndpoint, string)>();
        foreach (var endpoint in endpoints)
        {
            var parameters = openApi.Parameters(endpoint.Method, endpoint.Pattern);
            var routes = endpoint.RouteParameters.Count == 0
                ? [endpoint.Path(_ => "")]
                : idsA.Concat(idsB).Append(codeA).Append(codeB).Select(v => endpoint.Path(_ => v)).Distinct(StringComparer.Ordinal).ToList();
            foreach (var route in routes)
            {
                result.Add((endpoint, route));
            }
            var basePath = routes[0];
            foreach (var parameter in parameters.Where(p => p.In == "query"))
            {
                IEnumerable<string> values = (parameter.Type, parameter.Format) switch
                {
                    ("boolean", _) => ["true", "false"],
                    ("integer" or "number", _) => ["1", "50"],
                    (_, "uuid") => [idsA[0], idsB[0]],
                    _ => texts,
                };
                // Every value the document publishes for the parameter as well (each language, each
                // digit system): free text alone is refused by an enumerated parameter's validation.
                values = values.Concat(parameter.Enum ?? []);
                foreach (var value in values)
                {
                    result.Add((endpoint, $"{basePath}?{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(value)}"));
                }
            }
            if (catalog.Lists.FirstOrDefault(l => l.Endpoint == endpoint.Pattern) is { } list)
            {
                foreach (var column in list.Columns.Where(c => c.Groupable))
                {
                    result.Add((endpoint, $"{basePath}?take=50&groupBy={Uri.EscapeDataString(column.Key)}"));
                    result.Add((endpoint, $"{basePath}?take=50&search=a&groupBy={Uri.EscapeDataString(column.Key)}"));
                }
                foreach (var column in list.Columns.Where(c => c.Sortable))
                {
                    result.Add((endpoint, $"{basePath}?take=5&sort=-{Uri.EscapeDataString(column.Key)}"));
                    result.Add((endpoint, $"{basePath}?take=5&skip=5&sort=-{Uri.EscapeDataString(column.Key)}"));
                    // A sort with a search, as the client sends after a header click with a search
                    // typed (critic p05 round 7, plant L11: a total memo keyed by search, filter and
                    // sort, served only when both are present), both directions, and with a grouping.
                    if (list.SearchFields.Count > 0)
                    {
                        foreach (var sort in new[] { column.Key, "-" + column.Key })
                        {
                            foreach (var text in texts.Take(2))
                            {
                                result.Add((endpoint, $"{basePath}?take=5&search={Uri.EscapeDataString(text)}&sort={Uri.EscapeDataString(sort)}"));
                            }
                        }
                        result.Add((endpoint, $"{basePath}?take=5&skip=5&search=a&sort=-{Uri.EscapeDataString(column.Key)}"));
                    }
                    foreach (var group in list.Columns.Where(c => c.Groupable).Take(1))
                    {
                        result.Add((endpoint, $"{basePath}?take=5&groupBy={Uri.EscapeDataString(group.Key)}&sort={Uri.EscapeDataString(column.Key)}"));
                    }
                }
                // Offset pages (skip, a jump into the list), each judged right after the other tenant
                // opened the same query's first page and the same offset page (critic p05 round 5,
                // plant L10): plain, by each search text, by each grouping alone and with a search.
                result.Add((endpoint, $"{basePath}?take=5&skip=5"));
                if (list.SearchFields.Count > 0)
                {
                    foreach (var text in texts)
                    {
                        result.Add((endpoint, $"{basePath}?take=5&skip=5&search={Uri.EscapeDataString(text)}"));
                        result.Add((endpoint, $"{basePath}?take=2&skip=1&search={Uri.EscapeDataString(text)}"));
                    }
                }
                foreach (var column in list.Columns.Where(c => c.Groupable))
                {
                    result.Add((endpoint, $"{basePath}?take=5&skip=5&groupBy={Uri.EscapeDataString(column.Key)}"));
                    result.Add((endpoint, $"{basePath}?take=5&skip=5&search=a&groupBy={Uri.EscapeDataString(column.Key)}"));
                }
            }
        }
        return result.DistinctBy(r => r.Item2, StringComparer.Ordinal).ToList();
    }

    /// <summary>The body with trace ids removed and every date-time replaced by whose request it
    /// falls in (<see cref="RequestClock.Token"/>), so two answers that differ only by when they
    /// were made compare equal, and an answer carrying the time of the other tenant's request does
    /// not. Without a request context every time is <c>&lt;time&gt;</c>.</summary>
    internal static string Normalize(string text, AnswerContext? context = null)
    {
        try
        {
            return Scrub(JsonNode.Parse(text), context)?.ToJsonString() ?? "";
        }
        catch (JsonException)
        {
            return TimeRegex().Replace(text, m => TimeToken(m, context));
        }
    }

    private static JsonNode? Scrub(JsonNode? node, AnswerContext? context)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("traceId");
                foreach (var (key, child) in obj.ToList())
                {
                    obj[key] = Scrub(child?.DeepClone(), context);
                }
                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Scrub(array[i]?.DeepClone(), context);
                }
                return array;
            case JsonValue leaf when leaf.GetValueKind() == JsonValueKind.String:
                return JsonValue.Create(TimeRegex().Replace(leaf.GetValue<string>(), m => TimeToken(m, context)));
            default:
                return node?.DeepClone();
        }
    }

    private static string Short(string text) => text.Length <= 160 ? text : text[..160] + "…";

    /// <summary>Where <paramref name="text"/> first differs from <paramref name="other"/>: the
    /// whole answer when short, else the differing header line or the text around the first
    /// difference (a header leak sits past the body, beyond <see cref="Short"/>'s cut).</summary>
    internal static string Difference(string text, string other)
    {
        if (text.Length <= 160)
        {
            return text;
        }
        var lines = text.Split('\n');
        var otherLines = other.Split('\n').ToHashSet(StringComparer.Ordinal);
        var at = text.IndexOf(HeadersMark, StringComparison.Ordinal);
        if (at >= 0 && WithoutHeaders(text) == WithoutHeaders(other))
        {
            var differing = text[(at + HeadersMark.Length)..].Split('\n').Where(l => !otherLines.Contains(l)).ToList();
            return $"{Short(text[..Math.Min(at, 60)])} with header {(differing.Count == 0 ? "lines missing" : string.Join(" | ", differing.Take(3)))}";
        }
        var first = 0;
        while (first < text.Length && first < other.Length && text[first] == other[first])
        {
            first++;
        }
        var start = Math.Max(0, first - 60);
        return $"{(start > 0 ? "…" : "")}{text[start..Math.Min(text.Length, start + 160)]}{(start + 160 < text.Length ? "…" : "")}";
    }

    /// <summary>
    /// For an answer that changes from one call to the next even in a process only the judged
    /// tenant uses (a counter, a stamp): whether how it changes depends on the other tenant. The
    /// judged tenant sends the request three times in a row in the shared process (the change
    /// between answers must be the same twice, or the answer is merely unstable), then once more
    /// right after the other tenant sent it. A count of every tenant's requests changes by one
    /// alone and by two with the other tenant in between; a count of the tenant's own changes by
    /// one either way; a stamp or a fresh random value changes the same way either way. Run twice;
    /// only a difference seen both times is reported.
    /// </summary>
    /// <returns>The finding's wording ("changes by … alone, but by … when"), or null.</returns>
    private static async Task<string?> ChangeDependsOnOtherAsync(Func<Task<string>> judged, Func<Task> other)
    {
        string? first = null;
        for (var round = 0; round < 2; round++)
        {
            var one = await judged();
            var two = await judged();
            var three = await judged();
            var alone = Change(one, two);
            if (Change(two, three) != alone)
            {
                return null;
            }
            await other();
            var four = await judged();
            var with = Change(three, four);
            if (with == alone || (first is not null && first != $"{alone}|{with}"))
            {
                return null;
            }
            first = $"{alone}|{with}";
            if (round == 1)
            {
                return $"changes by {alone} from one answer to the next when sent alone, but by {with} when";
            }
        }
        return null;
    }

    /// <summary>How <paramref name="after"/> differs from <paramref name="before"/>: "nothing";
    /// for answers alike but for their numbers (Western or Arabic-Indic digits), the difference of
    /// each number that changed by its position ("#3 +2"); otherwise "the text".</summary>
    internal static string Change(string before, string after)
    {
        if (before == after)
        {
            return "nothing";
        }
        var numbersBefore = NumberRegex().Matches(before);
        var numbersAfter = NumberRegex().Matches(after);
        if (numbersBefore.Count != numbersAfter.Count || NumberRegex().Replace(before, "#") != NumberRegex().Replace(after, "#"))
        {
            return "the text";
        }
        var changes = new List<string>();
        for (var i = 0; i < numbersBefore.Count; i++)
        {
            var x = numbersBefore[i].Value;
            var y = numbersAfter[i].Value;
            if (x == y)
            {
                continue;
            }
            // The number by its place and the text just before it ("x-arabic-switches: "), so a
            // finding says which number moved.
            var from = i == 0 ? 0 : numbersBefore[i - 1].Index + numbersBefore[i - 1].Length;
            var lead = before[from..numbersBefore[i].Index];
            lead = lead.Length <= 24 ? lead : lead[^24..];
            var place = $"#{i} (after \"{lead.Replace("\n", " ", StringComparison.Ordinal)}\")";
            changes.Add(decimal.TryParse(Western(x), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var a) &&
                        decimal.TryParse(Western(y), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var b)
                ? $"{place} {(b - a >= 0 ? "+" : "")}{(b - a).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : $"{place} changed");
        }
        return string.Join(", ", changes);
    }

    private static string Western(string digits) =>
        new(digits.Select(c => c is >= '\u0660' and <= '\u0669' ? (char)('0' + (c - '\u0660')) : c is >= '\u06F0' and <= '\u06F9' ? (char)('0' + (c - '\u06F0')) : c).ToArray());

    [GeneratedRegex(@"[0-9\u0660-\u0669\u06F0-\u06F9]{1,27}")]
    private static partial Regex NumberRegex();

    /// <summary>Headers of the transport and of tracing, the same for every tenant by design or
    /// different on every answer by design; the body is compared on its own (its length too).</summary>
    private static readonly HashSet<string> TransportHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Date", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Server", "Via", "Age",
        "traceparent", "tracestate", "Request-Id", "X-Request-Id", "X-Correlation-Id", "X-Trace-Id",
    };

    /// <summary>The response's headers, normalised (<see cref="NormalizeHeaders(string, AnswerContext)"/>).</summary>
    internal static string NormalizeHeaders(HttpResponseMessage response, AnswerContext? context = null) => NormalizeHeaders(ResponseHeaders.Text(response), context);

    /// <summary>
    /// Response headers (<c>Name: value</c> lines, as <see cref="ResponseHeaders.Text"/> gives them)
    /// as compared: names in lower case, sorted; transport and tracing headers left out; the value
    /// of each credential cookie (<see cref="CredentialCookies"/>) replaced, every other cookie's
    /// value kept (critic p04 round 9); every time replaced by whose request it falls in
    /// (<see cref="RequestClock.Token"/>; <c>&lt;time&gt;</c> without a request context); and a
    /// declared print stamp (<see cref="ResponseText.PrintedAtHeader"/>) replaced. Every other header
    /// is compared as sent: a count, a version or a tag the product sends is part of the answer.
    /// </summary>
    internal static string NormalizeHeaders(string headers, AnswerContext? context = null)
    {
        var lines = new List<string>();
        foreach (var line in headers.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }
            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (TransportHeaders.Contains(name))
            {
                continue;
            }
            if (string.Equals(name, "Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                value = CookieValues(value, context);
            }
            else if (string.Equals(name, ResponseText.PrintedAtHeader, StringComparison.OrdinalIgnoreCase) &&
                     DateTimeOffset.TryParse(value.Split(';', 2)[0].Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at) &&
                     (at - DateTimeOffset.UtcNow).Duration() <= TimeSpan.FromDays(2))
            {
                value = "<printed-at>";
            }
            value = HttpDateRegex().Replace(TimeRegex().Replace(value, m => TimeToken(m, context)), m => TimeToken(m, context));
            lines.Add($"{name.ToLowerInvariant()}: {value}");
        }
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }

    /// <summary>A cookie's name and value at the start of a Set-Cookie value (one or several
    /// cookies joined by ", " as the client folds them).</summary>
    [GeneratedRegex(@"(?:^|(?<=,\s*))([!#$%&'*+\-.^_`|~0-9A-Za-z]+)=[^;,]*(?=;|,|$)")]
    private static partial Regex CookieValueRegex();

    [GeneratedRegex(@"(Mon|Tue|Wed|Thu|Fri|Sat|Sun), \d{2} (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) \d{4} \d{2}:\d{2}:\d{2} GMT")]
    private static partial Regex HttpDateRegex();

    /// <summary>
    /// The configuration of every host compared, read before and after each judged request: a
    /// value written into it while serving (a count, a last caller) is state every tenant of that
    /// process shares, whatever the answer shows (critic p04 round 8, plant L3 kept its count
    /// there). Writes through the indexer are refused by the product
    /// (<see cref="Erp.Kernel.Hosting.ReadOnlyConfiguration"/>); this sees a write that went
    /// straight into a provider as well.
    /// </summary>
    internal sealed class ConfigurationWatch
    {
        private readonly List<(string Name, Microsoft.Extensions.Configuration.IConfiguration Configuration, Dictionary<string, string?> Last)> _hosts = [];

        public int KeysWatched { get; private set; }

        public void Watch(string name, IServiceProvider services)
        {
            var configuration = services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var now = Read(configuration);
            KeysWatched += now.Count;
            _hosts.Add((name, configuration, now));
        }

        /// <summary>Reports every key that changed in any host since the last check, as done by
        /// <paramref name="label"/>.</summary>
        public void Check(State state, string label)
        {
            for (var i = 0; i < _hosts.Count; i++)
            {
                var (name, configuration, last) = _hosts[i];
                var now = Read(configuration);
                foreach (var key in now.Keys.Union(last.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    var before = last.GetValueOrDefault(key);
                    var after = now.GetValueOrDefault(key);
                    if (!string.Equals(before, after, StringComparison.Ordinal))
                    {
                        state.Findings.Add($"{label} changed the configuration of {name} while serving ({key}: {(before is null ? "unset" : $"'{Short(before)}'")} → {(after is null ? "unset" : $"'{Short(after)}'")}): configuration is shared by every tenant of the process");
                    }
                }
                _hosts[i] = (name, configuration, now);
            }
        }

        private static Dictionary<string, string?> Read(Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in Microsoft.Extensions.Configuration.ConfigurationExtensions.AsEnumerable(configuration))
            {
                values[key] = value;
            }
            return values;
        }
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?")]
    private static partial Regex TimeRegex();
}
