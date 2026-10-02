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
