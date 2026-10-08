# p04 — The isolation gates use and attack the app in Arabic too

Date: 2026-10-07. Piece: p04-shell, round 5. Status: accepted.

## Problem

The round-4 critic's biggest gap had two halves: a leak behind Arabic-Indic digits, and one
behind `language=ar`. The first half was a body value (`numerals: "arab"`) and is closed by the
enum-variant attack (`p04-shell-enum-variant-attacks.md`). The second half is not a value any
request carries. The kernel takes the request language from the signed-in user's preference
(`Languages.ForRequest`: the session's language claim, else Accept-Language). Every session the
gates signed in (tenant administrators, the read-only user, the role-less user, anonymous callers
without Accept-Language) was English, so every handler that branches on the request language
(problem texts, permission labels, the access summary, the default language of a printed list)
only ever ran its English branch under attack. A cache, a file or a memo kept on the Arabic
branch alone passed every gate, server and client.

## Decision

1. **An Arabic session for every tenant** (`tests/Erp.Gates.Tests/G1/ArabicSession.cs`): the
   seeded Arabic-speaking administrator (`admin.ar@<tenant>`), its digits preference set to
   `arab` through the product's own preferences endpoint, and Accept-Language `ar-AE` on every
   request; anonymous Arabic callers send Accept-Language `ar-AE`. A session counts only while
   `/api/auth/session` says `language: ar` and `numerals: arab`; otherwise the gate reports
   itself blind.
2. **The HTTP attack** (`G1HttpIsolationTests`): tenant B's activity has the Arabic administrator
   as a fourth actor (every read round, every touch before and after A attacks an endpoint, every
   open of the routes A is about to attack, the concurrent reader, and its own values in every
   query parameter). Tenant A attacks with an Arabic administrator and an anonymous Arabic caller:
   every route with B's ids, sent plainly (phase 1; the tenant-switch variants stay with the
   English sessions, see "Processor budget"), every GET parameter with a cross-section of B's
   values and every uuid route parameter with the sampled ids, each compared with a value that
   exists nowhere (phase 2), list
   answers judged both ways in Arabic (phase 2c), and Arabic write-after-write pairs (phase 1c)
   with the default body and with every enumerated field at its last documented value (Arabic
   with Arabic-Indic digits for the shell's preferences; the per-value variants stay with the
   cookie and bearer writers). The Arabic administrator writes records it created itself, since
   some (a saved list view) only their owner may see. An Arabic session's own writes (its
   preferences, through the attack's valid bodies) are undone after each endpoint, as a framed
   own write so tenant B's change tracking stays exact. The anonymous Arabic caller sends each
   permissioned endpoint one request (it only reaches the Arabic refusal), and published query
   values are left out of the Arabic oracle comparison, as in the English attack.
3. **Non-interference** (`NonInterference`): every read is compared once more between the two
   tenants' Arabic administrators (shared process right after the other tenant's Arabic request,
   against a fresh process only the judged tenant used), and every comparable write once more in
   Arabic with every enumerated field at its last documented value (Arabic with Arabic-Indic
   digits for the shell's preferences). The check is blind unless some Arabic answer has more
   Arabic letters than the English administrator's answer to the same request, which proves the
   Arabic branch ran.
4. **The client-state gate** (`clientIsolation.test.tsx`): the one-tab journey (B works and signs
   out, A works, then Back, Forward and reload into B's entries) runs in English and again with
   both sessions in Arabic with Arabic-Indic digits; B's markers are also looked for with their
   digits shaped (`Zx81` as `Zx٨١`). A planted Arabic-only memo of B's workspace name in the shell
   fails the Arabic run and passes the English one.
5. **Self-tests**: leaky bug 47 (a read with no body and no parameter that, only in Arabic, hands
   on the previous Arabic caller's e-mail through a temporary file) must be caught by the HTTP
   attack in both directions and only by Arabic sessions; bug 48 (the same, carrying only a
   number) must be caught by the non-interference check, only in Arabic.
6. **Ratchet**: `g1.arabicAttackRequests`, `g1.victimArabicRequests`, `g1.arabicWritePairs`,
   `g1.nonInterferenceArabicComparisons`, `g1.nonInterferenceArabicAnswers` and
   `g1.writeNonInterferenceArabicComparisons`, at the counts measured on the product.

## Processor budget

The first full verify with the Arabic sessions passed every test but used 9,520 processor
seconds against the ratchet's maximum of 9,000 (`verify.cpuSeconds`; the integration branch was
already at 8,961). The Arabic attacker had been a full copy of the English administrator in phase
1, tenant-switch header and query variants included. Those variants test where the tenant comes
from, not the language: the English sessions send them and the switch phase sends every header,
query name and cookie the app reads. The Arabic sessions now send every path of phase 1 plainly,
and phase 2 sends them the sampled ids in route parameters (each with its control value) rather
than every route value tenant B used, which phase 1 already sent in Arabic in every route. The
maximum is not raised.

## Alternatives considered

- *Switch the existing administrator to Arabic half-way through.* Each phase would then attack
  only one language, and a phase's English leaks would hide behind the switch.
- *Send Accept-Language: ar from the existing sessions.* The user's preference wins over the
  header, so a signed-in English user's request stays English; only anonymous requests change.
- *Every value of every parameter in Arabic as well.* Doubles the largest phase (some 100,000
  requests). The Arabic sessions send a cross-section of B's values (sampled ids, one value per
  text column, every published value) to every GET, which reaches each Arabic branch with B's
  data at a fraction of the cost; every route, write and list answer is attacked in full.

## Overlap with p00

p00 owns the shared G1 machinery and is closing the enum-value blind spot there in parallel.
This change is additive: a new helper file, a new actor appended to `TenantActivity` (index 3; the
read-only user stays at index 2 and is now named, not taken as the last), new attackers inserted
between the bearer token and the role-less user (the administrator stays first, the bearer second
and plain anonymous last), new report fields and ratchet keys. A merge with p00's version of
`TenantActivity`, `NonInterference` or `G1HttpIsolationTests` should keep both: p00's enum
handling and these Arabic sessions.
