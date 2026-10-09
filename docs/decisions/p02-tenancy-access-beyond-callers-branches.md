# p02 — A user who works in branches the caller does not is read-only to the caller

Date: 2026-10-08. Piece: p02-tenancy. Status: accepted (round 7).

## Context

Round 6 critic: a branch-limited administrator (AQZ-WH) opened a user who holds AQZ-WH and DEIRA-HQ.
The access screen answered `canEdit: true` and showed only AQZ-WH; any save, even an unchanged one,
was refused with `tenancy.grantBeyondOwn`, because the hidden DEIRA-HQ read as removed. The access
list's branch count also told the caller how many branches the user held, hidden ones included.

## Decision

- `CompanyAccessRules.RefuseUserAsync` adds a rule after the company rule: the user may work in no
  branch the caller does not (in every company of the caller's scope, the caller's access must cover
  the user's). Otherwise `tenancy.userBeyondOwnBranches` (403), and the access screen answers
  `canEdit: false` with `tenancy.access.readOnly.branches` (English and Arabic), so the screen
  offers nothing the server would refuse.
- The access list counts only branch rows whose branch the caller can see (the branch filter applies
  inside the count), so hidden branches are not even counted.

This is the existing company rule ("a user who works in companies you do not is read-only to you")
applied one level down; the caller still gives and takes their own branches on users within them.
