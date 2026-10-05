# p03 identity: the acting-on-grants gates aim at partial grants, not only at "everything"

Date: 2026-10-05. Piece: p03-identity, round 4. Status: accepted.

## Context

Critic p03 round 3 (verdict BLOCKED): every G2 probe that acts on a user or a role, or asks for a
grant, aimed at the strongest record only (the Administrator, a role holding the whole
catalogue), and every caller held identity permissions only. A check that compares only part of
the grants (plants P14 and P16: only `identity.*`) still refuses the Administrator, because the
Administrator also holds identity permissions the caller lacks. So a helpdesk clerk could take
over a "workspace manager" who held only tenancy permissions, and every gate passed. The screens
had the same blind spot (plant U2).

## Decision

1. **`GrantTargets`** (`tests/Erp.Gates.Tests/G2/GrantTargets.cs`) builds, for each caller, the
   targets a narrowed comparison could miss: every permission of the catalogue the caller lacks,
   alone; every module's permissions the caller lacks (disjoint from the caller's own); and, per
   module, the caller's own permissions plus one more (partial overlap). A check narrowed to any
   module, resource, action or prefix misses at least one of them.
2. Every acting-on-grants gate aims at them besides the record granting everything:
   `G2AccountTakeoverTests` (every non-GET user endpoint, users holding each target),
   `GrantBearingRecords` (every grant-bearing family found from routing and OpenAPI, records of
   that family granting each target), copying a role, and `GrantEscalation` (asking for each target
   through roles, permissions and roles in one company). Single-field requests (critic r2, P5) are
   also aimed at one record per other module, so a narrowing on one path only is found too.
3. **A module of a later wave** (`LaterLedgerModule`: `ledger.entries.read`, `ledger.entries.post`,
   `ledger.periods.close`) is hosted in the takeover environment, the way `ModulePermissionsTests`
   hosts contacts: the catalogue holds permissions no identity or tenancy check was written for.
4. **Gate self-tests**: the leaky module carries P16's shape (`/api/leaky/narrow-roles`: create,
   change, delete and copy compare only `identity.*`) and P14's (`/api/leaky/narrow-members`:
   create, edit and reset compare only `identity.*`). The self-tests require the gates to report
   them on the partial targets, and require the probe aimed at "everything" to stay blind to
   them (so the self-test proves why the new targets exist).
5. **Screens**: the screens gate opens users and roles granting each of `tenancy.tenant.read`,
   `tenancy.tenant.update`, `tenancy.access.update`, `lists.views.share` and `ledger.entries.post`,
   alone and on top of what the viewer holds; the web plant self-test adds U2 (users), U2-roles,
   company roles left out, and roles elsewhere ignored.
6. **Write oracle on names** (critic r3, L7): `G1WriteOracle` judges every text field named like a
   name (`nameEn`, `nameAr`, `displayName`, `legalNameEn`, view names …) besides e-mails and codes:
   tenant B uses the value through the endpoint, tenant A sends it next to a fresh one, the answers
   must match. Seeded names are not sent (both seeds share "Administrator", which a workspace may
   rightly refuse as its own). A leaky role-name registry (`/api/leaky/registered-roles`) is its
   self-test; applying the critic's L7 plant to the product fails it. Edits under a record that
   has no GET of its own (a copy) take fresh values; a singleton edit carries its version; list
   view bodies take their documented examples.

Ratchet keys added: `g2.takeoverPartialTargets`, `g2.takeoverModuleFieldVariants`,
`g2.copyPartialTargets`, `g2.grantBearingPartialTargets`, `g2.grantBearingModuleFieldVariants`,
`g2.grantEscalationPartialTargets`, `g2.companyRoleChecks`; `g1.writeOracleChecks` raised.

## Verified

With the critic's `P14-P16.diff` applied, all four acting-on-grants tests fail (231 problems);
with `apply-L7.py`, the write oracle reports `POST /api/identity/roles [nameEn]` and the copy;
with `U2-ui-beyond-own-identity-only.diff`, the new screens tests fail. Unplanted, all pass.

## Cost

About 900 more in-process requests in the takeover environment (tens of seconds) and around 40
more users created there (PBKDF2 hashing of invitation codes).
