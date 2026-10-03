# p05 — Saved views: a lists module with per-list endpoints guarded by the list's own permission

Date: 2026-10-03. Piece: p05-list-search. Status: accepted.

## Decision

A new module `lists` (`src/Modules/Lists`) owns the per-user state of every list: saved views in
`lists.saved_views` (row-level security, audited). For each registered list it maps, under
`/api/lists/{list key}`:

| Route | Permission | |
|---|---|---|
| `GET /definition` | the list's read permission | columns, types, labels, operators, choices, search fields, default sort, built-in views, `canShare` |
| `GET /views`, `GET /views/{id}`, `POST /views`, `PUT /views/{id}`, `DELETE /views/{id}` | the list's read permission | the caller's own views (others' personal views answer 404) |
| `GET /shared-views/{id}` | the list's read permission | a shared view |
| `POST /shared-views`, `PUT /shared-views/{id}`, `DELETE /shared-views/{id}` | `lists.views.share` | also needs the list's read permission: without it the list does not exist for the caller (404) |

A view stores visible columns, sort, filter, search and grouping, each validated against the list
(the same parser as the query contract). One personal default per user and list and one shared
default per list (partial unique index); names unique per owner and list. Optimistic concurrency on
`xmin`. Built-in views (`ListPreset`) live in code with translated labels. Each workspace is seeded with
one shared "Team view" per list built from the same sort, filter and grouping the API document gives
as examples for view bodies.

## Why

Every endpoint declares exactly one permission (p00), so per-list routes carry each list's own
permission instead of one generic route with a hidden second check: whoever can read a list can see
its definition and keep personal views of it, nobody else can, and G2 proves it by granting exactly one
permission. Sharing changes what everyone sees, so it is a separate, grantable permission. A module of
its own keeps the table, migrations and strings out of the kernel; the query engine itself stays in the
kernel because every module uses it.

The view request schema is documented per list (column keys and groupable columns as enums, a valid
sort and filter as examples), so API clients and the G1 activity that writes valid bodies on the
victim's records know what passes validation. The seeded team view holds the same example values,
so values written from the examples are never values only one tenant holds.
