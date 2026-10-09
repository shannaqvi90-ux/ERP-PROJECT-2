# p02 — The branch line on an Arabic screen, and branch codes from the branch's own name

Date: 2026-10-08. Piece: p02-tenancy. Status: accepted (round 7).

## Context

Round 6 critic: on an Arabic screen, saving a new company moved the focus to the branch line's
English name, already holding the English company name, so an Arabic branch name typed there was
saved as the English name and the Arabic name stayed empty. Codes suggested for new branches
repeated the company's code (CRITIC, FALCON), because they were made from the start of the name,
which is the company's name.

## Decision

- On an Arabic screen the branch line puts the Arabic name first, starts it with the company's
  Arabic name and " - ", and gives it the focus after the company is created (the caret after the
  prefix). Like the English name, an Arabic name left at the prefix alone is saved as the company's
  Arabic name. English screens are unchanged (the comparison driver's path stays the same).
- When no branch code is typed, it is made from the branch's own part of the name: the text after
  the last " - " ("Falcon Logistics LLC - Jebel Ali Branch" gives JEBEL), or HQ for a branch named
  after its company alone (its head office); then the usual uniqueness suffix.
