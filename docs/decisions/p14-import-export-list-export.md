# p14 — Export a filtered list

Date: 2026-11-18. Piece: p14-import-export, round 1. Status: accepted.

## Decision

- **Already built, reused.** "Export any list as CSV or XLSX with its current filters and columns" is the reports module's printable
  list (`GET /api/reports/lists/{list key}?format=csv|xlsx|pdf`, p06): search, filter, sort, grouping and visible columns of the screen,
  every row (100,000 contacts stream line by line), English or Arabic, tenant- and permission-checked (G1 `G1PrintFileTests`, G2 on the
  list's own read permission). Every list's toolbar has "Print or export" (Alt+Shift+R). p14 adds nothing to that path and does not
  copy it: a second exporter would be a second place to keep tenant-safe.
- **What p14 checked.** The export-filtered-list comparison task exports the contacts tagged "Government" (thousands, more than the
  page on screen); the list export covers every matching row. The comparison driver for our product is not written in round 1: the
  tag filter is a column filter on the Tags column, and the driver must find its controls by role in the p18 design that is being built
  in the same workflow.

## Why

Two exporters of lists would double the surface the isolation gates must attack, and the existing one already meets the spec.
