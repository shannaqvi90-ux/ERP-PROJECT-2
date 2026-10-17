# p04 — G1 on the shell's own answers, raw; the palette's further permissions

Date: 2026-10-17. Piece: p04-shell (round 10). Status: accepted.

## The gap (critic p04 round 9)

The shared non-interference check (`tests/Erp.Gates.Tests/G1/NonInterference.cs`) replaces the
value of every `Set-Cookie` with `<value>` and every time in any header or body with `<time>`
before it compares two answers, and its change probe compares the same normalised answers. The
critic planted on `PUT /api/identity/me/preferences`, the shell's own language and digits switch:

- L4: the count of every tenant's Arabic switches, kept in a temp file, sent back as a cookie value;
- L6: the time of anyone's previous switch, sent in a response header.

All 169 gate tests passed, and on the running build tenant A watched tenant B's switches.

The fix in the shared machinery belongs to p00-foundation (lead's routing for this run: p04 does not
edit `NonInterference.cs`). This record is the shell's side: a check of its own endpoints that
blanks nothing, so the shell is covered now, whatever the shared check does, and keeps a stricter
layer after p00's fix lands.

## Decision

`tests/Erp.Gates.Tests/G1/ShellAnswers.cs` and `G1ShellAnswersTests`:

- **What is judged.** The shell's server surface: `GET /api/auth/session`, `POST /api/auth/sign-in`,
  `POST /api/auth/sign-out`, `PUT /api/identity/me/preferences`. A test reads the shell's sources
  (`web/src/modules/shell/**`, `web/src/kernel/session.tsx`, `preferences.ts`) and fails if the shell
  calls an API path the check does not judge, or the check judges one the shell no longer calls.
- **Every byte.** Status; every header except the transport's own (`Date`, `Content-Length`,
  `Transfer-Encoding`, `Connection`, `Keep-Alive`, `Server`); every cookie's value and its
  attributes; every leaf of a JSON body. Only the values of the two reviewed credential cookies
  (`erp_session`, `erp_device`, random by design) are left uncompared, and their shape (length,
  kinds of characters) and attributes are compared. A reviewed list of values random by design
  exists and is empty: no value of the shell's answers needed it.
- **Judged by how it changes, not blanked.** One tenant (X) makes the same turn at an even pace
  of 4 s: a warm-up, three turns alone, then a fourth just after the other tenant (Y) made the same
  turn three times. A turn is a preferences write and the session read the shell makes after it
  (each of the four language/digits combinations, Arabic included), or a whole sign-in, session
  read and sign-out. Each value of X's fourth turn is split into text, numbers (Western or
  Arabic-Indic digits) and times (ISO 8601 or HTTP dates), and:
  - the text must equal X's own;
  - each number must move by the same step as it does along X's own turns alone (a version X's own
    writes raise moves by one either way; a count of every tenant's switches moves by four);
  - each time must be fixed if it is fixed alone, or else lie at the same distance from the start
    of X's request as alone (tolerance 250 ms plus the larger of the time's resolution and the
    spread alone, plus the longest request). X's turns are evenly paced, so "X's previous turn"
    stays 4 s back, while "anyone's previous turn" moves to Y's, a moment back. 4 s keeps an HTTP
    date (one second's resolution) of Y's turn apart from one of X's previous turn;
  - a time with millisecond resolution that falls inside one of Y's requests and none of X's is a
    finding by itself;
  - a value that appears or disappears after Y's turns, or that changes along X's own turns in a
    way none of the rules can judge (uneven steps, changing text), is a finding unless reviewed as
    random by design.
  Both directions.
- **Self-test.** `ShellPlantModule` wraps the real endpoints on a second host over the same
  database and keeps every tenant's activity in a temp file (outside the process, so the process
  state gate cannot see it): L4 (switch count in cookie `erp.tip`), L6 (previous switch time,
  ISO 8601, header `X-Arabic-Previous-Switch`), L6b (anyone's last sign-out as the session's
  `Last-Modified`, an HTTP date) and L7 (every tenant's sign-ins in cookie `erp.seen`). The check
  must name each in both directions and nothing else on the planted host. Run locally: all four
  caught both ways, plus L6 by the inside-Y's-request rule; the real host has no finding.
- **Ratchet.** New minimums: `g1.shellAnswerComparisons`, `g1.shellAnswerHeaderValuesCompared`,
  `g1.shellAnswerNumbersJudged`, `g1.shellAnswerTimesJudged`, `g1.shellAnswerCredentialCookiesShaped`,
  `g1.shellAnswerEndpoints`, `g1.shellAnswerPlantsCaught`, `g1.shellAnswerPlantCookieValuesCompared`.

### Why a separate check and not only the shared one

The shared check compares every endpoint of the app once alone and once after the other tenant;
it has to blank values that differ on every call for reasons nobody controls there. On four
endpoints the shell owns, a paced sequence can judge each changing value by its step and its
distance in time, so nothing needs blanking. Its cost is small (about 180 requests, two seconds of
processor time; most of its 3 to 4 minutes are the paced waits, beside the gate project's other
tests).

## The palette's further permissions (critic p04 round 9, plant P4)

Removing the palette filter's check of an action's `requires` (`paletteActions.ts`) passed all 470
web unit tests at the judged commit. Now:

- the filter test checks, for every action, that it is not offered without each permission in its
  `requires`, and the ratchet requires at least one such action (`g2.paletteActionsWithRequires`);
- a test opens the palette, in English and in Arabic, as a user holding `identity.users.update`
  without `identity.signIns.read` and requires that Allow sign-in again is not offered, with the
  control that it is offered once `identity.signIns.read` is added;
- `palette-plant-self-test.mjs` plant PA13 removes the check and requires those tests to fail.

## Processor time

Measured on its own outside the verify (`dotnet test --filter G1ShellAnswersTests`): about 30 s
of user processor time for the test process, most of it the test host's start and the template
copy, against 160 s of wall time. The palette's new test adds about 2 s to the web unit stage and
PA13 one more planted run of two tests in the palette self-test (about 10 s).
