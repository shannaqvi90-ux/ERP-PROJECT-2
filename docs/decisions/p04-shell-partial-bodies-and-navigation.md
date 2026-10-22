# p04: G1 judges the bodies the shell sends; random trace ids; screens open on every navigation; lists fit the page

Date: 2026-10-22. Piece: p04-shell (round 12). Status: accepted.

## 1. The gap (critic p04 round 11): partial preference bodies

Every G1 check that called the shell's own write, `PUT /api/identity/me/preferences`, sent a whole
body (language and numerals). The shell never sends that on its main path: Alt+L, the top bar's
language button and My account send `{language}` alone, the digits switch sends `{numerals}`
alone, and a change saved again after a reload sends any subset (`PreferenceChange =
Partial<Preferences>`, web/src/kernel/preferences.ts). The critic's plant SL1 kept the digits
anyone last chose in a temp file and answered a language-only switch with them. Every G1 check
passed while tenant A's answer showed tenant B's last choice.

The shared half (the product-wide write-variant generator, `NonInterference.cs`,
`TenantActivity.cs`) belongs to p00-foundation's round 13, as the lead routed it. This piece does not
edit those files. This record covers the shell's own half.

### Decision

`tests/Erp.Gates.Tests/G1/ShellAnswers.cs`:

- **Bodies come from the published schema, not a list kept here.** `BodyVariantsAsync` reads the
  request schema of `PUT /me/preferences` from the running app's OpenAPI document. It builds every
  subset of its fields with every combination of each field's documented values: `{language}` x 2,
  `{numerals}` x 2, `{language,numerals}` x 4, and the empty body `{}` (refused: nothing to change).
  A field without two documented values throws as a blind spot. It is never skipped.
- **The other tenant writes what X does not hold.** For each body, X first stores a whole state
  (`Prepared`): the body's fields, and for each field left out a value of its own, taking each value
  in turn so that both English and Arabic sessions occur. Y's three turns write, in every field, the
  value X does not hold. The first turn sends the whole differing body and the next two send each
  field alone, as the shell itself would. A value of X's answer taken from anyone but X (a fill
  from a shared default, the last value anyone chose, a cache) then shows as a change. The same
  pacing and judging rules as before apply (every header, cookie and body leaf; numbers by step;
  times by distance).
- The existing whole-body turns, where Y makes the same turn, stay unchanged. They catch counters
  and times of the other tenant's identical switches, which a differing write would not move.
- **The client and the check cannot drift apart.** `Every_body_the_shell_sends_to_its_own_write_is_judged`
  checks four things in the web sources:
  - `Preferences` names exactly the schema's fields, in both directions.
  - `PreferenceChange` is `Partial<Preferences>`.
  - The preferences path is written only in kernel/preferences.ts, and its PUT sends that change.
  - Every body written out at a `savePreferences(...)` call, or at the switches' `persist(...)`,
    names only those fields.

  Every non-empty subset of the client's fields, and `{}`, must also be a judged shape.
- **Self-test plant SL1** (`SelfTests/ShellPartialPlantModule.cs`). It keeps the value anyone sent
  last for each field in a temp file, outside the process, and answers a write that leaves a field
  out with that value. The stored row stays right. The check names `body.numerals` for
  `{language:"en"}` in an English session and `body.language` for `{numerals:"arab"}` in an Arabic
  one, for both tenants and nothing else. The whole-body turn of the same run stays quiet, as it did
  in round 11.
- The ratchet gets new keys: `g1.shellAnswerBodyShapes` 4, `g1.shellAnswerBodyVariantsJudged` 18,
  `g1.shellAnswerTraceIdsShaped` 10 and `g1.shellAnswerPartialPlantsCaught` 2. Four keys are raised:
  comparisons 1,422 to 3,604, header values 176 to 464, numbers 86 to 240 and times 18 to 36.

## 2. The request trace id was a counter shared by every tenant

The empty-body turn is the first shell answer that carries a problem's `traceId`, and judging it
found the following. ASP.NET Core's `HttpContext.TraceIdentifier` is a counter that every request
of the process moves (Kestrel: a global connection counter and the request number on the
connection; the test server: a global request counter). The product wrote that counter into
every audit row (`correlation_id`) and every log line. Tenant A's audit trail therefore counted
how many requests other tenants made between two of A's own.

The shared non-interference checks drop `traceId` before they compare, so no gate judged it. That
drop is in p00's files; this record names it for p00 and changes nothing there.

**Decision.** The kernel pipeline (`ErpPlatform.UseErpPlatform`) sets the trace identifier first
thing:

- normally, the request's W3C activity id, which has a random span id and a random trace id
  unless the caller sends its own `traceparent`;
- when there is no activity, 32 random hex digits.

The id is the same one ASP.NET's problem writer puts in `traceId`, so answers, logs and audit rows
now carry one id. `ShellAnswers.RandomShapes` compares `body.traceId` by that exact shape only. Any
other shape (a counter) is judged as numbers. The main test also checks that the audit rows of the
run's preference writes have correlation ids of the random shape, all different.

## 3. Screens open on every navigation (scope gap, critic round 11)

The palette opening a second record of the screen on show, or G then U from a record, changed the
address only. The screen read the address when it mounted, and it stayed mounted. Ctrl+S then
saved the first record.

**Decision.** `useNavigationKey()` (kernel/router) is a counter that every `navigate()` and every
Back or Forward moves. A screen's own `replaceAddress`/`replaceState` does not move it. The shell
renders `<Screen key={navigationKey} />`, so the screen opens again on the new address. Because
the screen now opens again even when only the query changes, `navigate()` always asks about
unsaved changes first (it used to skip the question when only the query changed).

Tests:
- web unit `navigationReopen.test.tsx`, English and Arabic: record to record, G U to the list,
  Back, role to role. It fails without the key.
- e2e `navigation.spec.ts`: the palette from viewer to noaccess (search, row, record and e-mail
  field), G U from the record, Back, and Read-only to Staff, in English and Arabic.

Why not have screens subscribe to `erp:navigate` and reset their state? Each screen (lists,
records, reports, every later module's) would need its own reset code, and one that forgot would
bring the bug back. A key on the screen covers all of them in one place.

## 4. A list on paper fits the page; an open record prints whole

On A4 the users grid needed 743 px (English) and 789 px (Arabic) against about 703 to 718 px of
page. In Arabic the Roles column was off the page and times were cut. The record panel printed at
the screen's height, cut off.

**Decision** (kernel/lists/lists.css `@media print`, ListView):

- `ListView` sets `--list-print-columns` on every row: each column's share with no minimum and no
  selection column. On paper the header and rows use it.
- On paper the selection cell takes no track and no room and shows nothing. It stays in its row (positioned at
  the row's start, zero size, hidden), so the one-line check of every list screen (a cell inside its row) holds.
  Nothing has an ellipsis: values wrap and a row grows to hold them.
- The rows on screen print one after the other, not at their virtual positions.
- The list body is a block. The record follows the list from a new page at the page's width, with
  no height and no overflow cut.

e2e `print-paper.spec.ts`, English and Arabic, at the A4 printable width (186 mm, 703 px), under
print media:
- every header and row cell lies inside the page, and no cell or value is cut;
- at least 6 headed columns are shown;
- an open record spans at least 90 % of the page, is not cut, and has no field off the page.

With the round-12 print rule removed from the live stylesheet, 39 cells of the Arabic list are off
the page or cut, so the test catches the old layout.

## 5. Help sheet

- The PgUp/PgDn row now reads "Move a page up or down" / "الانتقال صفحة إلى الأعلى أو إلى الأسفل",
  in the order the keys are shown.
- Ctrl+P ("Print this screen") is listed under General.
- The AppShell unit test checks both, in both languages.

## Processor time and wall time this round adds

The new gate work mostly waits: paced turns with about 10 requests each. Measured alone on this
machine:
- The shell answers main test went from about 3 min to 7 min 28 s (484 requests).
- The new plant self-test takes 1 min 37 s (104 requests).
- The body-shape test takes 0.4 s.

The processor time of about 600 more HTTP requests to an in-process host is a few seconds. The
shell answers collection runs alone after the parallel collections in the `suite` process. Its
extra wall time (about 6 min) fits inside the slack before `self-http` ends: in the round-11
critic's run, suite took 41 min and self-http 56 min. The web unit test adds about 4 s, and the
three e2e tests about 10 s. The kernel trace-id middleware costs one random draw per request.

## Comparison tasks

Every task compared in round 11 was won on every metric (switch to Arabic, reach a screen by
keyboard, open the third user, new user by keyboard). No tie needed a decision under the owner's
tie rule.
