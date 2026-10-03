# p03 identity: G2 sends one request per writable field, and keeps a reviewed permission map

Date: 2026-10-03. Piece: p03-identity. Status: accepted.

## Context

Critic p03 round 2 planted P5: `PUT /api/identity/users/{id}` skipped the access check on the
target when only the e-mail changed. Every gate and identity test passed, because the takeover and
grant-bearing gates sent one body only (the record with a changed name). A clerk holding
`identity.users.update` could move the Administrator's sign-in to their own address. The same
round showed two more blind spots: plant P2 (sign-in history guarded by `identity.users.read`) was
caught only because `identity.signIns.read` became unused, and plant U1 (New role shown without
`identity.roles.create`) passed every web test.

## Decision

- **One field at a time** (`tests/Erp.Gates.Tests/G2/FieldVariants.cs`). For every non-GET endpoint
  that acts on a grant-bearing record (found from routing and the OpenAPI document, as before), and
  for every endpoint acting on a user aimed at the real Administrator, the gates take each writable
  property of the request schema and send two more requests: that property changed alone to another
  valid value, and that property left out. The rest of the body is the record's own values (an edit)
  or fresh valid values (an action). Each request is aimed at the strong record (403 required,
  record byte-for-byte unchanged) and at the weak control (2xx required for a change; for an
  omission the control may refuse with 400, and then the strong request must be refused too).
- Values come from the schema: another enum value, the other language, a flipped boolean, a fresh
  address in the tenant's domain, Arabic text for `*Ar`, a valid password. Grants are changed by
  removing one (strong) or by adding or removing the caller's own role or permission (weak).
- A property the gate cannot vary is reported as a problem ("extend FieldVariants"), never skipped.
  Only the concurrency token `version` is copied unchanged.
- **P5 as a self-test**: the leaky module gains a grant-bearing `members` family whose PUT skips
  the access check on an e-mail-only edit; the self-test requires the gate to report exactly the
  `[email changed]` request and nothing for the name, roles or plain edit.
- **Reviewed map** (`tests/Gates/endpoint-permissions/*.txt`): a module lists the route prefixes it
  covers and, per endpoint, the permission it must declare with the reason. Any endpoint under a
  covered prefix that is missing, or declares another permission, fails; so does a line that matches
  no endpoint. Identity's map covers `/api/identity/` and `/api/auth/`. Other modules can add their
  own file; nothing outside a covered prefix is affected, so parallel pieces are not broken.
- **Screens**: every toolbar button of Users and Roles is compared with every identity permission
  taken away in turn; exactly the actions needing that permission may disappear. A plant runner
  (`web/scripts/identity-plant-self-test.mjs`, run by `./erp verify`) applies U1 and four sibling
  plants to a copy of the sources and requires the screen gate to fail on an assertion for each.

## Ratchet

`g2.grantFieldVariantsChecked`, `g2.takeoverFieldVariantsChecked` and
`g2.reviewedPermissionEndpoints` are new minimums, set at this round's counts.
