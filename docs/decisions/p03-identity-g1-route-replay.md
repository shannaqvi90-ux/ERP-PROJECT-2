# p03 identity: the G1 attack replays tenant B's own route values

Critic p03 round 1's biggest gap: a per-user-id cache on `GET /api/identity/users/{id}/access`,
held in a dictionary the endpoint lambda captures (plant T2) or in a static (T1), handed tenant B's
roles and permissions to tenant A, and the HTTP attack passed.

## Why the attack missed it

Tenant B's activity opened each route with one of its own ids, preferring a record it had just
created; tenant A attacked with up to five ids per table, sampled in id order. A cache keyed by id
alone only answers for the ids that were cached, so the two sets rarely met.

## Decision

- `TenantActivity` records every value tenant B put in a route (its ids that answered and the
  records it created). Phase 1 of the attack sends each of them, on every route, as tenant A, in
  addition to the sampled ids; phase 2 sends them through every route parameter (with the
  differential check against a value that exists nowhere).
- Right before tenant A attacks a GET route, tenant B's administrator and read-only user open it
  with every value tenant A is about to send (`TouchEveryAsync`): whatever a handler keeps per id
  then holds tenant B's answer for exactly the ids tenant A sends, and the body (and every header)
  is judged for tenant B's markers.
- Ratchet: `g1.victimRouteValuesReplayed`, `g1.victimPreTouches`.
- Self-tests (`LeakyModule`): a person's access view cached per id in a captured dictionary and in
  a static, on a route whose ids tenant B creates itself; the attack must report both.
- The process-state inventory (p00 round 3) lists closure fields of endpoint delegates and already
  reports plant T2's captured dictionary; the HTTP attack now catches it by its effect as well.

## Unique indexes

Plant U (e-mail unique across the platform) was caught only by chance. `G1UniqueIndexTests`
requires every unique index on a tenant table to have `tenant_id` among its keys, except the
primary key on the server-generated id and the reviewed entries in
`tests/Gates/global-unique-indexes.txt` (session token hashes, workspace codes); self-test plants
the global e-mail index. Ratchet `g1.uniqueIndexesChecked`.
