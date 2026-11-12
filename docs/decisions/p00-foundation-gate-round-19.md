# p00-foundation round 19: four gate faults seen on a correct product

Routed round (serial-20). Every change makes a gate stricter or more exact. No product code changed.

## 1. G1CompanyIsolationTests and paging numbers (take, skip)

Seen in the critics' runs of serial-17, 18 and 19: `GET /api/audit/log?take=20002` and `?skip=20002`
flagged. The log clamps `take` to 200 and grows while the gate's own tests write, so the two answers
of a pair (a company-Y number against a control number) are windows that differ at their edge and
carry a different `next` cursor. Nothing told the two numbers apart.

Choices:
- A paging number (take, skip, limit, offset, top, size, page, pageSize, perPage, count, first;
  `MovingLists.SendsAsPagingValue`) chooses a window of a list; it matches no text. When its pair
  differs after the second asking, `MovingLists.PagingExplainedByMovement` judges the four answers in
  time order by shape (same properties; a list may be empty in one) and by every number outside the
  lists (a total only grows or only shrinks). Rows are not compared; they are still judged for company
  Y's markers in `Judge`. Anything else (another status, a property for one side, a total that
  follows the side, not JSON) is still an oracle. A byte-exact pair is exact as before.
- Rejected: a snapshot bound (occurred_at/id) per list, because it needs each list's own parameters
  and the gate would stop being generic; a repeatable-read pair, because the two asks are two HTTP
  requests of the product.
- The stored-text excuse (`Stored.Any(s => s.Contains(value))`) no longer applies to a paging number.
  A stored text that happens to hold the digits "20002" (a hash, a phone) excused the pair; found
  while planting the leak below.
- Self-test: plant P1 (`/api/leaky/paged-companies?skip=`) looks the offset up among every company's
  PO box (scope widened) and answers only a total (1 for Y's PO box 20002, 0 for a number that exists
  nowhere), no marker. `The_company_attack_catches_an_endpoint_that_widens_the_company_scope` must
  report it as an oracle. Unit self-tests in `MovingListsSelfTests`.

## 2. ListContractGateTests, "no row has a word in search field recordCode"

The window of the newest 150 rows can hold no coded record when parallel gate tests write users and
roles. The test now walks the list newest first (up to 100 pages of 200) for a row that has a word in
the field and searches the whole list for it; the check itself (the search finds the row, every hit
holds every word) is unchanged. A list that holds no such row anywhere still fails.

## 3. GateSelfTests.The_HTTP_attack_catches_planted_header_route_and_body_leaks (planted e-mail lookup not caught once)

The lookup answers tenant B's id. The marker check removes what the request sent and what tenant A
stored in its own records before it looks for B's markers; a stored text made of hex digits only
(a number such as "123" or "20002", a word such as "dad") also occurs inside a random id, and
replacing it there broke B's id, so the leak was not seen in the runs where the random id held one.
`MarkerScrub.Scrub` now replaces the values that are whole ids first, sets the ids left in the text
aside, removes the other values and puts the ids back (used by the HTTP and the company gate).
Strictly stricter: an id in an answer is found unless the request sent that id or the attacker stored it.
Unit self-tests in `MarkerScrubSelfTests`. The self-test was run ten times in a row on the final code with no failure (12 to 13 minutes each).
An earlier version of the scrub (placeholders) failed one of its runs with an index error when a printed
answer held the placeholder's control characters; replaced by scrubbing between ids.

## 4. G1ListVolumeTests, answers that are not 200

The failure line now names the request, the status code with the seconds the request took and the
start of the server's error (`ListAnswers.GetWithReasonAsync`), so a slow answer and a refused one
can be told apart. No limit changed.

## Not done

(5) custom-column attacks in G1HttpIsolationTests: not cheap (needs p09's registration of cf keys in
the attack's parameter classes); left for the gate backlog row.

## Cost

Test time added: the planted endpoint adds about 800 requests to one company self-test; no product
test and no full-suite gate gains a request. Processor time of the round's own targeted runs is in the
notes for the integrator.
