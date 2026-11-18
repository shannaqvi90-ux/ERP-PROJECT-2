# Keeping sign-in records (personal data)

Checked: not yet (see below). Decided: the owner, 2026-11-18 (needs-human #7). Owning piece:
p03-identity, with the deletion run as a p12 background job.

## The decision

1. Each sign-in attempt record (time, user, outcome, client IP address, browser) is kept for
   1 year from the attempt, then deleted automatically.
2. Only roles given the sign-in log permission may see these records; the standard read-only role
   may not.

## Owner's decision, 2026-11-18

Asked to choose a period (90 days, 180 days, 1 year, or another), the owner answered: "1 saal"
(1 year).

## Source (to be recorded before the deletion is built)

IP addresses are personal data under the UAE Personal Data Protection Law, Federal Decree-Law
No. 45 of 2021. Under CLAUDE.md rule 8, the lead records the law's current text on keeping personal
data (with its official link and the date checked), and any executive regulation in force, here
before the deletion is built. If the law sets a different limit, it goes back to the owner.
