# p10 round 2: G1 control collision and switching the reset back

## G1 random control matched tenant A's own prefixes
The G1 HTTP differential sends tenant B's value and a random control of the same shape. A case-insensitive
"contains" filter on the sequences list matched A's own two-letter prefixes (RV-, QT-, ...) with a random
two-letter control about once in 85 attacks, a false positive. The shared machinery is p00's and is not
touched. The p10 gate seed now gives tenant B's distinct values a digit (BQ73-, /BR58, BPO7-{YYYY}-,
BDN4-{BRANCH}-): the control replaces the digit with another digit, so it cannot equal any of A's
letter-only prefixes. The gate is not loosened; the seed is stricter about what a control can hit.

## Switching every year, never, every year in one year
When the target period's counter already existed (older than the counter that ran meanwhile), Update kept
it, so the preview showed a number already given and every take was refused (numberTaken). Update now
raises the older counter to the running one (next value, last issued) when the reset changes. Test:
Switching_from_every_year_to_never_and_back_in_the_same_year_never_offers_a_number_again_that_the_other_run_moved_past.

## Not changed
Tax document numbering rules (UAE VAT Executive Regulation Art. 59(1)) are a human gate: listed for the owner.
Processor time added by this round: none measurable (one seed edit, one test).
