# p04-shell: response headers, changing answers and the configuration in G1 non-interference

Round 9, answering the p04 round 8 verdict (BLOCKED, plant L3).

## What was missing

Plant L3 kept a count of every tenant's Arabic switches in `IConfiguration` and sent it back
only in a response header (`X-Arabic-Switches`) of `PUT /api/identity/me/preferences`. Nothing of
tenant B's showed: a number. Three gates were blind to it at once:

- `NonInterference` compared `"<status> <body>"` only, never the headers.
- Even with headers compared, the count goes up on every call in the process only the judged
  tenant uses too, so the comparison called it "unstable" and moved on (an unstable answer was not
  judged at all, only counted against a 25 % blindness limit).
- `G1ProcessState` walks static fields and singletons of the product's own types; the
  configuration is a framework object no product field reaches.

## Choices

1. **Headers are part of every compared answer** (`NonInterference.Answer`), for GETs and writes,
   in English and Arabic. Normalised (`NormalizeHeaders`): names lower case and sorted; transport
   and tracing headers dropped (`Date`, `Content-Length`, `Transfer-Encoding`, `Connection`,
   `Keep-Alive`, `Server`, `Via`, `Age`, `traceparent`, `tracestate`, `Request-Id`, `X-Request-Id`,
   `X-Correlation-Id`, `X-Trace-Id`); each `Set-Cookie` value replaced (name and attributes kept);
   ISO and HTTP dates replaced; a valid `X-Erp-Printed-At` print stamp replaced. Everything else
   (a count, an ETag, a version, `X-Total-Count`, a cookie of a new name) is compared as sent.
   Writes get their headers through `TenantActivity.WriteThroughWithHeadersAsync` (an additive
   overload; the actor records the last response's headers).
   Ratchet: `g1.nonInterferenceHeadersCompared` (header lines in the compared true answers).

2. **A changing answer is judged by how it changes** (`ChangeDependsOnOtherAsync`). When the
   fresh process answers differently from one call to the next, the shared process is asked three
   times in a row by the judged tenant (the change between answers must repeat, else it is merely
   unstable), then once more right after the other tenant sent the same request. `Change` describes
   a change by the difference of each number by position (Western or Arabic-Indic digits), or
   "the text" when anything but numbers moved. One more alone and two more after the other tenant
   is a finding; a per-tenant counter (one more either way) and a fresh random value (the text
   either way) are not. Run twice; only a difference seen both times is reported. This needs no
   knowledge of where the count is kept: configuration, a file, a static, a cache, a database
   sequence.

3. **The configuration is watched** (`ConfigurationWatch`): every key of the shared process and of
   both fresh processes is read before and after each judged request and after each of the
   tenants' own writes before the comparison; any change is a finding naming the request.
   Ratchet: `g1.nonInterferenceConfigurationKeysWatched`.

4. **The product refuses runtime writes to its configuration** (`Erp.Kernel.Hosting.ReadOnlyConfiguration`).
   `AddErpPlatform` puts a guard provider first among the configuration sources; `UseErpPlatform`
   seals it once the app is built. A write through the indexer (root, section, a request's
   services) then throws before any provider takes the value; reads are unchanged (the guard holds
   no keys). Writing straight into a provider still gets past it, which is why choice 3 exists.
   The guard's one field is a latch (open, then sealed once at start-up); the process-state gate
   does not flag it. Gate: `G1ConfigurationTests`; unit tests: `ReadOnlyConfigurationTests`.

5. **Plants kept as self-tests** (`LeakyModule`, `NonInterferenceSelfTests`): bug 66 is plant L3's
   shape (count in the configuration, written past the guard, sent in a header on the Arabic write
   only); bug 67 counts every caller's reads outside the process and sends the count in a header;
   bug 68 sends the change since the previous caller's directory size in a header with a body the
   same for everyone (stable in the fresh process, so the header comparison alone catches it).
   The self-test requires the change finding both directions, the configuration finding, the header
   finding both directions, and nothing on the English variant.

## Evidence

- Real product, `G1NonInterferenceTests` with the new checks: passed (5,092 read and 442 write
  comparisons, 4 unstable answers, all `POST /api/identity/roles/{id}/copy` creating a new record
  with a new id each time, which the change probe judges as "the text" both ways).
- `NonInterferenceSelfTests` (process `self-noninterference`): passed, every plant above found.
- The critic's exact plant L3 diff and a variant that writes past the guard were run against
  `G1NonInterferenceTests`; results are in the round's notes for the integrator.

## Processor time

The header comparison adds no requests. The change probe adds 10 requests per answer that changes
in the fresh process (4 such answers in the product today, so about 40 requests out of about
23,000). The configuration watch reads every key of three hosts once per judged request (about
8,000 reads of a few hundred keys, in memory). Measured: `G1NonInterferenceTests` ran in 8 min 42 s
in a run beside other agents' verifies (the critic's r8 run took 15 min under heavier load); the
added work is well under 1 % of the test's requests. Estimated added processor time per verify:
under 20 s.
