# p00 — Hard gates as tests, with a ratchet

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

The gates live in `tests/Erp.Gates.Tests` and run in every `./erp verify`:

- **G1 database**: every table is tenant-owned or on `platform-tables.txt`; every tenant table has
  `tenant_id uuid NOT NULL`, RLS enabled and forced and exactly the standard policy; extra policies
  only for reviewed `erp_auth` lookups; the app role is not superuser/BYPASSRLS/member/owner and
  cannot `SET ROLE`, create objects, disable RLS or drop policies; for every tenant table, bound as
  tenant A, the app role sees only A's rows and cannot update, delete, insert or move rows into B;
  every FK between tenant tables includes `tenant_id`; SECURITY DEFINER functions are exactly the
  reviewed list, owned by `erp_auth`, with a pinned `search_path`.
- **G1 HTTP**: endpoints are enumerated from the running app's routing (not from source). For each,
  four attackers (A admin by cookie, A admin by bearer token, A user without roles, anonymous) send
  requests with every tenant-B id in route values, tenant-switch headers, query fields and request
  bodies built from the OpenAPI schema. No response may contain any B id or canary; no 5xx; a
  before/after checksum of every B row in every tenant table must be identical. Endpoint families
  marked Export/Import/Job/File need a registered `IIsolationProbe` or the gate fails.
- **G1 self-tests**: a deliberately leaky test-only module (header, route and body tenant bugs) and
  a planted table without RLS must each be caught, proving the gate is not blind.
- **G2**: every endpoint has exactly one catalogued permission or is on the reviewed anonymous
  allowlist; every catalogue permission is used; anonymous gets 401 and a role-less user 403 from
  every permissioned endpoint; for each permission, a user granted only it opens exactly the
  endpoints declaring it and sees exactly its menu entries; escalation attempts are refused; the
  host refuses to start with an undeclared endpoint.
- **G3**: `tests/Gates/g3-clean-clone.sh` (`./erp verify --clean-clone`).
- **Rule gates**: licence allowlist (NuGet and npm, transitive), no float money (DB, C#, TS),
  English/Arabic parity (server and web, Arabic text really Arabic, every code and label present),
  audit trigger on every tenant table and field-level rows for API and raw-SQL writes, OpenAPI
  covers every API endpoint with its permission.
- **Ratchet**: `gauntlet/ratchet.json` holds minimum counts (tables, endpoints, requests, strings,
  packages checked, tests passed) and maximum counts (anonymous endpoints, SECURITY DEFINER
  functions, audit exemptions). Tests assert against it, and `build/ratchet-check.mjs` fails verify
  if a minimum fell, a maximum rose or a key vanished relative to the committed file, or if any test
  failed or was skipped.

## Why

- The owner's bar: gates are written first and may only get stricter (CLAUDE.md rule 9).
- Enumerating from the running app means a new endpoint is attacked the moment it exists.

## Round 2 additions (2026-10-02)

The round 1 critic planted an endpoint that looked users up by e-mail through the reviewed
sign-in function and an endpoint that let a user create users with roles beyond their own; the
gates missed both. The gates now cover them:

- **G1 HTTP, tenant B values in every parameter.** `VictimValues` reads tenant B's real values
  from the database (every id, and every text value tenant A does not also hold: e-mails, names,
  codes, hashes). Phase 2 sends them through every query parameter the OpenAPI document lists
  (plus guessed names such as `email`, `code`, `name`, `q`) and every route parameter; phase 3
  puts every B text value into every string field of every request body. Values the attacker
  successfully stored in its own tenant, and values echoed back from the same request, are not
  counted as leaks; ids and canaries are always leaks.
- **G1 HTTP, existence oracles.** Every GET in phase 2 is repeated with a value of the same
  shape that exists nowhere (letters and digits randomised, punctuation kept). Different status
  or body (trace ids removed, sent value masked) fails the gate, so "does this e-mail exist in
  another tenant" endpoints are caught even when they return no id.
- **G1 reviewed lookups.** `SqlTrace` listens to Npgsql's ActivitySource and records which
  endpoint (or the authentication handler) ran each statement naming a reviewed SECURITY
  DEFINER function. `tests/Gates/security-definer-callers.txt` lists the reviewed callers and
  source files; another caller, or another file under `src/` naming the function, fails the gate.
  The gate also fails if the trace never saw the reviewed callers (a blind trace).
- **G2 grant escalation.** Every endpoint whose request body has `roleIds` or `permissions` is
  found from OpenAPI. A user holding only that endpoint's permission (plus read access to roles
  and users) asks it to grant the Administrator role and every permission: it must answer 403 and
  nothing may change; the same request granting only what the caller holds must succeed.
- **Self-tests** plant each of these faults in the test-only leaky module (e-mail lookup,
  existence oracle, undocumented query name, body lookup, role-granting endpoint).
- **Speed.** Phase 2 runs four requests at a time (GETs first, then other methods, so no write
  lands between a GET and its control). Phase 2 uses five ids per table and the full text-value
  set for documented parameters, and a cross-section for guessed names and catch-all routes.

## Round 5 additions (2026-10-04)

- **G1 by value.** The trace judges the tenant every `set_config`/`SET` inside a request sets,
  read from the statement's own parameters, against the signed-in principal (or the tenant the
  kernel's session declared for sign-in and the session lookup); input enumeration by product code
  is reported; the methods naming `app.tenant_id` are reviewed one by one in compiled code. See
  `p00-foundation-g1-tenant-by-value.md`.
- **Speed.** Phases 1 and 3 and the company attack send several requests at a time where order
  does not matter (eight, `AttackParallelism`); the long self-tests run in test processes of their own; one PostgreSQL per test
  process with template databases. See `p00-foundation-verify-speed.md`.
