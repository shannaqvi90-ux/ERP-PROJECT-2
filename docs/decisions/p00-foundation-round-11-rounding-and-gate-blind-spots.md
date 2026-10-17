# p00 round 11: Money rounds once; four gate blind spots closed

Date: 2026-10-17. Piece: p00-foundation. Status: accepted.

## 1. Money follows the owner-signed rounding rule (critic p00 round 10, biggest gap)

`docs/compliance/vat-rounding-and-currency.md` rule 2 (signed off 2026-10-10): rounding happens
once, on the stated figure; lines and tax-category subtotals keep full precision. Until now
`Money.Add` summed base amounts already rounded (three lines of USD 1.00 at 3.6725 gave AED 11.01,
the rule gives 11.0175, stated 11.02) and a line could not carry more decimals than its currency.

Decision (details in `p00-foundation-money.md`): a `Money` is unrounded (lines, subtotals; the
default) or stated (`State()`, the one rounding). Unrounded figures add at full precision and the
base of the sum is rounded once from the exact sum; stated figures add as stated; mixing the two is
refused. `Money.Unrounded` takes up to 10 decimals; `RoundingAmount()` / `BaseRoundingAmount()`
report the e-invoice "Rounding Amount". Amount column `numeric(28,10)` and a `stated` flag column;
no table used the mapping yet, so no migration. Every earlier `MoneyTests` test is unchanged and
passes; new tests pin the critic's case and each part of the rule. Nothing new is decided about the
law: this implements the rule as signed off, so no new human gate.

Why one type with a flag rather than a second "line amount" type: the critic's reproduction built
lines with `Money.Create`, which is what every module will reach for first. Making the default
arithmetic the rule-following one, and the stated figure an explicit step, means the obvious code
is the correct code.

## 2. G1 non-interference compares cookie values and attributes times (p04 round 9, routed)

The normaliser blanked every `Set-Cookie` value and every time in headers and bodies, so a
process-wide count in a cookie, or the time of the other tenant's last action, passed.

Decision: times are attributed, not blanked. Every request the check sends is timed, and so is
each tenant's writing before the comparison; a time in an answer becomes `<time:this>` (inside the
request answered), `<time:own>` (an earlier request of the same tenant), `<time:other>` (the other
tenant's request) or `<time>` (inside none: seeded data, a cookie's expiry, the same in both
answers because both read one database). The interval of a time is its precision plus the
database clock's measured distance from the test's (most stamps are transaction times). An
interval touching windows of two kinds is `<time:ambiguous>`; the comparison is then run again
with requests paced apart by more than that interval, and only the paced run's result counts. Times
coarser than a second are not attributed. Cookie values are compared as sent, except the two
credential cookies (session and trusted device), named from the product's own constants.

Why not the critic's other option (keep every time and judge changing ones by how they change):
the times a write answers (its own updated-at) differ on every call by an amount that depends on
scheduling, so the change probe would call them unstable or, worse, call scheduling noise a
finding. Attribution keeps those equal (`<time:this>` in both) and still tells a time of the
other tenant's request apart, with no flakiness from timing.

Plants added to the self-test module: bug 69 (every caller's visits in a cookie value), bug 70
(the previous caller's visit time in a header), bug 71 (the same to the whole second in a body,
attributed by the paced re-run). The self-test asserts each is found in both directions, that cookie
values were compared, times attributed and a paced run made.

## 3. G1 tenant source rule: any unproved `IgnoreQueryFilters` (p02 round 10, routed)

The rule matched only `IgnoreQueryFilters()` and calls naming the tenant filter, so
`IgnoreQueryFilters(ownRow)` with the tenant filter's name in a variable (plant L2b) passed. Now every
mention of `IgnoreQueryFilters` is a use unless its argument is a collection expression of nothing
but `ModuleDbContext.CompanyFilterName` and `TenancyDbContext.BranchFilterName`. A new rule
`filter-name` reviews where filter-name constants and those two types are declared or aliased, so
the trusted names cannot be shadowed, and a test checks neither equals the tenant filter's name.

## 4. G1 process state walks service factories (p03 round 11, routed)

The inventory walked singletons, statics, framework holders and endpoint delegates, but not the
factory delegates of service registrations, so `AddScoped<AccessMemo>(_ => new AccessMemo(shared))`
over one dictionary (plant L2) was not named. Every factory delegate the product wrote, of any
lifetime, keyed or not, is now a root of the reachable-state walk and of the state fingerprint. It
found one real capture in the kernel (the data source factory captured the builder's
configuration); that factory now resolves `IConfiguration` from the container instead.

## Processor time

The machinery added here costs little processor time: a binary search per time in a compared
answer, one more root per product factory, a regular expression change. The paced re-runs wait
rather than compute. Measured: this round's verify (slot taken 2026-10-17T19:45:22Z, load up to
62 from other agents' verifies) passed in 3,707 s with processor time 9,770 s (dotnet 7,523, web
1,926, e2e 287, timing 35) against the 10,500 s maximum, inside the 9,218 to 10,181 s range of recent
verifies. The .NET stage's 7,523 s is level with round 10's critic run (7,516 s), so the round adds
no measurable processor time. The product's non-interference test attributed 3,540 times with none
ambiguous and no paced re-run (8 min 24 s alone); the self-test with plants 69 to 71 took 14 min 7 s
alone (16 min 52 s in round 10's critic verify).
