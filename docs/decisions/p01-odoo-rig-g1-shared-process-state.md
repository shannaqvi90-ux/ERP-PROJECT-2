# p01-odoo-rig: G1 covers leaks through shared process state

Date: 2026-10-03. Context: critic p01 round 1 planted a three-line static cache in
`GET /api/tenancy/tenant`; the G1 gate and the whole .NET suite still passed while tenant A's
administrator received tenant B's workspace record.

## Decision

Two layers, both in `tests/Erp.Gates.Tests/G1/`.

1. **Tenant B uses the product while tenant A attacks it** (`TenantActivity.cs`, wired into
   `IsolationAttack`). Tenant B's administrator (cookie and bearer) and read-only user:
   - write their own records through every endpoint that changes data (creates, then edit-and-save
     round trips of the record's own GET, then deletes of what they created); each write must
     succeed, so its handler runs to the end;
   - read every GET, with their own ids in routes and their own values in every documented query
     parameter, before the attack (so B's answers sit in any search-keyed cache before A sends the
     same values);
   - read each endpoint right before and right after tenant A attacks it, and every GET after each
     of A's writes;
   - read in a background loop while tenant A's parameter and body attacks run (the two tenants
     really share the process at the same moment);
   - read everything once more at the end.
   Every response to tenant B is judged for tenant A's markers (ids, long tenant-only values),
   excluding B's own values the attack stored in A. A blind activity (an actor signed out, no read
   succeeded, no concurrent request) fails the gate.
2. **Process-state inventory** (`G1ProcessStateTests.cs`). Reflection over the host's assemblies
   and every loaded module's assembly: every static field, and every instance field of every
   singleton the app registers, must be immutable (read-only and of an immutable type, checked
   recursively for the product's own types) or reviewed in `tests/Gates/process-state-allowlist.txt`
   with a reason. Registering a cache service (memory, distributed, hybrid, output, response cache)
   also needs review. Stale entries fail.

Self-tests plant a first-writer static cache and a singleton that hands each caller the previous
caller's list; the HTTP attack catches both (the singleton in both directions) and the inventory
flags both. The critic's plant A4 now fails both layers (214 leaks; one unreviewed static field).

The differential (oracle) check now scrubs only JSON string values, and scrubs both the tenant B
value and the control value from both answers, because tenant B's own writes add short values
such as "user" that also occur inside ordinary labels.

## Why not alternatives

- Only a source scan (Roslyn or regex): misses state hidden behind singletons registered by
  factories and caches added through DI; reflection over the running app's registrations sees
  what actually runs, and needs no new dependency.
- Only the behavioural attack: a cache that is filled under conditions the attack never meets
  would pass; the inventory forces a human-readable reason for every piece of shared state.
