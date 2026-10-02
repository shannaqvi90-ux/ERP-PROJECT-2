# p00 — Modules register their searchable lists

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

A module registers each searchable list from its own folder with
`module.List(new ListDefinition(key, titleKey, permission, endpoint, columns, searchFields, …))`.
A definition names the GET endpoint that returns pages of rows, the permission that endpoint
declares, the columns (JSON key, web label key, type, sortable, filterable), the fields the
free-text search matches and the default sort.

Checks: when registered (key starts with the module name, `/api/` endpoint, search fields and
default sort name real, sortable columns, no duplicate columns); at host start-up (a GET endpoint
with that route exists and declares the same permission, or the host refuses to start); in the
gates (title and column labels exist in the web strings; the list opens with its permission
alone and is 403 without it).

## Why

The list/search framework (p05), saved views, import/export (p14) and custom fields (p09) all
need to know what lists exist, what they show and who may read them, without reading other
modules' code. Registering metadata next to the endpoint keeps a module self-contained; the
start-up check stops a list from drifting away from the endpoint and permission that serve it.
