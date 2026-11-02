# p10 round 4: set-based repeat check, immutable issued numbers

## Repeat check (owner decision #32, rule 5)
`SequenceEndpoints.RepeatedNumberAsync` used to run one query per period, date and branch code
(about 24 per branch; 17 s at 2,403 branches), which ended a G1 request past the HttpClient's 100 s.
It now reads three things once (the sequence's counters, the company's branches, the company's
issued numbers of the type that start with the pattern's fixed beginning) and compares in memory:
the printed (prefix, suffix) pairs of every period go in a dictionary with the lowest next value,
and each issued number is looked up at its digit runs (at most 18 digits, so a few hundred lookups
per number). The statement count no longer depends on branches or periods
(`RepeatCheckTests.The_repeat_check_reads_the_same_few_statements_with_three_branches_or_five_thousand`
counts statements, not seconds).

## Closed hole
The periods now include every period a number was already given in, read in the new reset's shape
(counter missing there: it starts at 1). A change yearly to monthly (same printed text) or never to
yearly that could give a number of an earlier month or year again is refused with
`numberingPatternRepeatsNumber` (English and Arabic text existed). Tests: probe-backdated-month and
probe-backdated-switch.

## Issued numbers cannot be changed (rule 2)
Migration `IssuedNumbersAreNeverChanged` revokes UPDATE and DELETE on `numbering.issued_numbers`
from `erp_app`. Code only inserts (`ON CONFLICT DO NOTHING`). A test connects as the app role and
expects `insufficient_privilege` for both. Foreign-key cascades run as the table owner and are
unaffected.

## Preview
`.nb-number` is `white-space: nowrap` so the previewed document number stays on one line in both
languages.

## Cost
Adds four small tests to the Numbering test project (one container already shared); no new
dependency. Comparison task left as it was (p01 frozen).
