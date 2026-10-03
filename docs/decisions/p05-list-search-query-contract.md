# p05 — One list query contract, served by a binding next to each list

Date: 2026-10-03. Piece: p05-list-search. Status: accepted.

## Decision

Every registered list endpoint accepts the same query parameters and answers in the same page:

| Parameter | Meaning |
|---|---|
| `search` | Words (at most 8, 200 characters). Every word must occur, in any case, in at least one search field. |
| `filter` | Filter language: `column op value` joined by `and`, `or`, `not` and parentheses. Operators `eq ne lt le gt ge contains startswith endswith in`, `is null`, `is not null`. Text in single quotes (a quote doubled), numbers with a dot, dates and times as ISO 8601 text. Text comparisons ignore case. At most 25 conditions, 8 levels, 2,000 characters. |
| `sort` | Sortable columns separated by commas (at most 4), `-` for descending. Default from the registration. |
| `after` | Keyset paging: the `next` of the previous page. |
| `skip`, `take` | Offset paging (the grid uses it to jump); `take` 1–200, default 50. `after` and `skip` together are refused. |
| `groupBy` | A groupable column: the page also carries every group of the matching rows with its count and the totals of number and money columns. |

Page: `{ items, total, next, groups }`. `total` counts every matching row; `next` is null on the last page.

A module registers a list with `module.List(ListBinding<TRow>.For(definition, row => row.Id).Column(key, row => row.Value)…)`.
The binding maps columns to expressions over the row type and turns a request into one SQL query for
the count, one for the page and one for the groups (EF Core, every request value a SQL parameter,
so one cached plan per query shape). The endpoint reads its binding back with
`catalog.ListBinding<TRow>(key)` and calls `QueryAsync(source, request, http, ct)`. The row id always
breaks ties, in the direction of the first sort key, so keyset paging is exact. Cursors are base64url
JSON of the sort, the last row's key values and its id; a cursor made for another sort is refused.
PostgreSQL orders nulls last ascending and first descending; the keyset predicate follows the store.
A small, bounded list (a workspace's roles) may run the same contract in memory with
`.InMemory(reason)`.

Errors are 400 validation problems naming the parameter (`errors.filter`, `errors.sort`, …) with
codes `validation.list.*` in English and Arabic. Messages never repeat what was sent, only column keys
the registration declares, so an answer cannot echo or differ by another tenant's text.

Checks: the binding is checked at registration (every sortable, filterable, groupable, totalled or
searched column is bound to a value of a type that suits the column type); the host refuses to start
when a registered list has no binding; the gates check every list through its real endpoint
(`ListContractGateTests`), its indexes (`ListIndexGateTests`), attack it with the other tenant's
values in filters, grouping, sorting and forged cursors (G1, phase 2b) and open it with its
permission alone (G2).

## Why

p00 registered lists with sortable and filterable columns that their endpoints ignored. The framework
had to serve those promises once, for every module, and the gates had to hold each list to them.
A text filter language travels in an address, a saved view and an API call alike, and is easy to read
in a log; JSON filters in a query string are neither. Keyset paging keeps deep pages as fast as the
first (seek, not skip); offset paging stays for jumping to a scroll position. Building expressions
over the module's own entities keeps tenant isolation where it already is (the DbContext's tenant
filter and row-level security): the framework never sees or sets a tenant.

Alternatives: OData or GraphQL (large dependencies, far more surface than the screens need, and a
second query model next to EF Core); Gridify / Sieve-style libraries (each adds its own syntax and
would still need the tenant-safe, gate-checked registration around it).
