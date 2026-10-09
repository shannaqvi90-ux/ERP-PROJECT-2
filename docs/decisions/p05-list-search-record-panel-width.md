# p05 — a screen's record form grows with the window (round 7)

Date: 2026-10-09. Piece: p05-list-search (round 7). Status: accepted.

## Context

The list framework's record panel was `minmax(260px, 340px)` beside the list at every window
width. Screens that put a whole form in it (`renderRecord`: a role's permission matrix, a user's
"What they can do", a company's form) were cut: critic p03 round 5 found the matrix headers read
"Creat", "Chan", "Delet" and "Granted by" ran past the panel's edge at 1366, 1440 and 1920 px, in
English and Arabic. The tenancy screens had already widened their own panel (48%).

## Decision

- A list that shows a screen's own form (`renderRecord`) gets `has-form`: the panel is
  `minmax(min(380px, 50%), 48%)` of the list body, so it grows with the window. The framework's
  own read-only facts panel keeps its narrow width (a label and a value per line).
- The role matrix lets an area name wrap between words in its column, and its area and other
  columns (24% and 22%) leave each action column 13.5%, so "Change" and "Delete" fit whole at
  1366 px.
- End-to-end, in English and Arabic: at 1366, 1440 and 1920 px no header of the role matrix or of
  "What they can do" is cut or outside the panel, the panel never scrolls sideways, it is at least
  380 px wide and at 1920 px more than 200 px wider than at 1366 px. The header test that checks
  every list header stays inside its column now also runs with a record's form open (p02 round 6
  saw the companies list's legal-name headers overlap with the form open; on this branch they do
  not).
