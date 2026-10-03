# p03 identity: acting on what grants access

Round 2, after critic p03 round 1 (plants P1, P2 and the Delete role button).

## Decision

1. **A role is changed, copied or deleted only by someone who holds every permission it grants.**
   Before, only copying and deleting checked this, and renaming a stronger role passed. Changing a
   role acts on everyone who holds it (renaming the finance role "Leavers" is as harmful as
   clearing it), so the rule is the same for every action: `identity.roleBeyondOwn` (403). Adding
   or removing a permission the caller lacks stays `identity.grantBeyondOwn`.
2. **The same rule already held for users** (acting on someone whose roles grant more than you
   hold is taking the account over: `identity.userBeyondOwn`). It now also covers deleting a user.
3. **The screens mirror the rules exactly** (`roleActions`, `userActions` in
   `web/src/modules/identity/model.ts`): every action needs its own permission, nothing acts on a
   stronger record, nothing administrative acts on oneself, and a record beyond one's own is
   shown read-only with a sentence saying why. The server still decides.

## Gates

- **G2 `GrantBearingRecords`** (`tests/Erp.Gates.Tests/G2/GrantBearingRecords.cs`). A record is
  grant-bearing when its collection is created with a grant field (`roleIds`, `permissions`), so
  roles, users and any later module that hands out access are found from the running app and its
  OpenAPI document, not from a list. For every endpoint that acts on one record (anything but GET
  under `collection/{id}`), a caller holding exactly that endpoint's permission aims it at a record
  granting everything (403 and the record reads back unchanged, still there after a delete) and at
  a record granting only what the caller holds (2xx, proving the 403 came from the grant check).
  Self-test: `DELETE /api/leaky/roles/{id}` deletes any role (plant P2's shape) and is reported.
  Ratchet `g2.grantBearingActionsChecked`.
- **G2 read permissions never change data** (`ReadPermissionWrites`, from p00 round 3) already
  catches plant P1 (unblock guarded by `identity.signIns.read`).
- **Screens hide exactly what a missing permission refuses**
  (`web/src/modules/identity/screens.test.tsx`): for every action of the Roles and Users screens,
  a user holding everything but that action's permission does not see that action and still sees
  the others; a stronger role or user shows no actions. The Delete role plant fails two of them.
  End to end: a user who may change roles but not delete them sees no Delete role and the API
  answers 403 (`tests/e2e/specs/identity.spec.ts`).

## Alternatives not taken

- Letting anyone with `roles.update` rename any role: simpler, but a role's name is what other
  administrators assign by, so renaming is a change to everyone holding it.
- A separate "manage stronger roles" permission: an escalation path by design.
