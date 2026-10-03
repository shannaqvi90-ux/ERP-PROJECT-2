# p00 — The tenant source gate counts reviewed uses per file

Date: 2026-10-03. Piece: p00-foundation (round 4). Status: accepted.

## Context

Critic p00 round 3, plant T1: a header-driven tenant switch whose `set_config` sat in
`ErpDbSession.cs`, a file already reviewed for `set-config` and `tenant-setting`. The source gate
reviewed by file and rule, stopped at the first match, and passed it; only the run-time HTTP attack
caught the plant.

## Decision

Each entry of `tests/Gates/tenant-bypass-sources.txt` now records the exact number of matches of
its rule in its file (comments included): `<rule> <path> <uses>  # reason`, one when left out.
More matches than reviewed fail with every line listed (a new use must be reviewed and the count
raised); fewer fail too (the count must come down, or a later use could slip in under it).

## Why

A file-level review approves the uses that were read, not every future use in the same file.
