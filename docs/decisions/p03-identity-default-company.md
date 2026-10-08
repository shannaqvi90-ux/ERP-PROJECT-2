# p03 identity: an administrator chooses where another user starts work

Date: 2026-10-05. Piece: p03-identity, round 4. Status: accepted.

## Context

The piece asks for a per-user default company; the wave 1 integrity check found that only the
user could choose their own (the top bar's switch, `PUT /api/tenancy/workplace`). p02 keeps the
working company in `tenancy.user_workplaces`; identity may not read or write tenancy's tables.

## Decision

- Tenancy offers the contract `IUserWorkplaces` (additive, `Erp.Modules.Tenancy.Contracts`):
  read another user's working company and the companies of the caller's scope they may work in,
  and set it with the version read. It refuses a company the user does not work in (or an
  inactive one), a user who also works in companies outside the caller's scope (their default may
  be one of those), and a stale version. Changing the company clears the branch; the session then
  picks the user's first branch there, as it does after a switch without a branch.
- Identity serves it on the user: `GET /api/identity/users/{id}/default-company`
  (`identity.users.read`) and `PUT …/default-company` (`identity.users.update`), with identity's
  acting-on-a-user rule first (never on oneself, never on a user holding more than the caller),
  so the generic G2 takeover and grant-bearing gates cover it with no list of endpoints: they give
  their callers and controls access to two companies and vary `companyId` one field at a time.
- The user panel shows "Starts work in" (their first company, or one of theirs) and saves it with
  the record.

## Alternatives not taken

- A field in tenancy's company access body: p02 is changing that endpoint in parallel (its
  version check) and the screen belongs to p02.
- A second copy of the default in identity: two sources of one fact.
