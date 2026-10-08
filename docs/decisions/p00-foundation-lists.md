# p00 — Modules register their searchable lists

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

A module registers each searchable list from its own folder with
`module.List(new ListDefinition(key, titleKey, permission, endpoint, columns, searchFields, …))`.
A definition names the GET endpoint that returns pages of rows, the permission that endpoint
declares, the columns (JSON key, web label key, type, sortable, filterable), the fields the
free-text search matches and the default sort.

Every list endpoint answers in one page shape, `{ "items": [...], "total": n }`, whose rows carry
every registered column as a JSON property (`/api/identity/users` pages with `skip`/`take`;
`/api/identity/roles` returns all of a workspace's few roles as one page).

Checks: when registered (key starts with the module name, `/api/` endpoint, search fields and
default sort name real, sortable columns, no duplicate columns); at host start-up (a GET endpoint
with that route exists and declares the same permission, or the host refuses to start); in the
gates (title and column labels exist in the web strings; the list opens with its permission
alone and is 403 without it; `RegistrationGateTests` checks from the OpenAPI document that the
endpoint returns `{ items, total }`, that the rows have every column and that the search parameter
is documented when search fields are registered).

## Why

The list/search framework (p05), saved views, import/export (p14) and custom fields (p09) all
need to know what lists exist, what they show and who may read them, without reading other
modules' code. Registering metadata next to the endpoint keeps a module self-contained; the
start-up check stops a list from drifting away from the endpoint and permission that serve it.

## Integrity check, wave 0 (2026-10-03)

The roles endpoint returned a bare array while the users endpoint returned a page; the list
framework could not read both the same way. Roles now return the same page shape, and the gate
above keeps every future list on it.

## Integrity check, wave 1 (2026-10-04)

The page shape and paging above are superseded by p05's list query contract
(`p05-list-search-query-contract.md`): every registered list endpoint takes `search`, `filter`,
`sort`, `after`, `skip`/`take` and `groupBy` and answers `{ items, total, next, groups }`, and a list
is registered with its query binding (`module.List(ListBinding<Row>.For(…))`) or served by another
module's list (`servedBy`). The roles list is no longer "all roles as one page": it runs the same
contract in memory (`.InMemory(reason)`). `ListContractGateTests` holds every registered list to it.
