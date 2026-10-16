# p00 — G2 checks permissions once per session carrier

Date: 2026-10-11. Piece: p00-foundation. Status: accepted.

## Context

A session token reaches the API two ways: the `erp_session` cookie (the browser app) and an
`Authorization: Bearer` header (API clients; any user gets a token from
`POST /api/auth/sign-in` with `issueToken: true`). Critic p00 round 9 planted, in the kernel's
`PermissionAuthorizationHandler`, a rule that let any signed-in Bearer caller pass every permission
check (plant P3). Every test passed: G2's deny-by-default and grant-exactly-one matrices signed in
only with cookies, and Bearer sessions appeared only for administrators, who hold every permission.

## Decision

- **One list of carriers in the kernel.** `Erp.Kernel.Security.SessionTokenCarriers.All` (Bearer,
  then cookie) is the only way the session handler reads a token (`ReadToken` walks the list; the
  Bearer header still wins over the cookie, so behaviour is unchanged).
- **The matrices run per carrier.** `CarrierMatrix` (tests/Erp.Gates.Tests/G2) runs "a user
  whose roles grant nothing is denied everything" and "granting exactly one permission opens
  exactly its endpoints" once per listed carrier, from a single sign-in whose token each carrier's
  client presents its own way (cookie plus `X-Erp-Request`, or the Bearer header alone). A third
  matrix sends a token the server never issued through every carrier: 401 everywhere.
  Ratchet: `g2.credentialCarriers` minimum 2.
- **A new carrier cannot be left out.** `ErpTestEnvironment.ClientCarrying` throws for a carrier
  name it does not know, so adding a carrier to the kernel list fails G2 until the tests can send
  a token that way. A G2 test sends a real token through sixteen places a carrier might hide
  (query strings, other headers, other cookies, other Authorization schemes) and requires each to
  be ignored, so a carrier added to `ReadToken` outside the list is caught too.
- **Static second layer.** `G2SessionCarrierSourceTests` scans product code for reads of the
  Authorization header, the word Bearer and the session cookie, and allows them only in files and
  counts reviewed in `tests/Gates/session-token-reads.txt`. The round-9 plant is a new, unreviewed
  line there. A regex can be evaded (a header name built at run time), which is why the matrices,
  not this scan, are the gate.
- **Self-test.** `The_carrier_matrices_catch_a_permission_bypass_for_bearer_callers` hosts the
  planted bypass (`BearerBypassModule`: an extra authorization handler that succeeds any
  permission for a Bearer caller) over the G2 environment's database, and requires both matrices
  to report it for the Bearer carrier and for no other.

## Why this shape

- Taking the list from the kernel, rather than from a list in the tests, means the gate follows
  the product: the place that decides how a token is read is the place the gate enumerates.
- One sign-in per user, presented through both carriers, keeps the extra cost to the requests
  themselves (no second password hash per user). On a quiet machine the grant-exactly-one test
  took 21 s for both carriers.

## Checked

With the critic's P3 hunk applied and `CRITIC_PLANT=P3`: the deny matrix failed
(`[bearer] GET /api/identity/companies: 200`, ...), the grant matrix failed
(`[bearer] identity.profile.update: GET /api/identity/roles answered 200 (expected 403)`, ...),
and the static scan reported `src/Kernel/Erp.Kernel/Security/Permissions.cs:79`. Without the plant
all pass.
