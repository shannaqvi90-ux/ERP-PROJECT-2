# p03 identity: "all that match" applies the one-user access rule, set-based

Round 6, after critic p03 round 5 (BLOCKED: finding R1 and plant P2).

## Decision

- **One rule, two shapes.** `POST /api/identity/users/matching/active` (the users list's "all
  that match") refuses exactly the users `UserAccess.RefusalAsync` refuses for one user, written
  as one set-based condition (`UserBulkEndpoints.BeyondCallerAsync`) so 100,000 matching users
  are still changed by one `UPDATE`. A user is left alone when they hold:
  - a workspace-wide role granting a permission the caller does not hold everywhere;
  - a role in one company granting a permission the caller holds neither everywhere nor in that
    company (the caller's own roles in that company count there);
  - any role in a company the caller does not work in. Row-level security and the company filter
    hide those rows from the caller, so the user's stored count of company roles is larger than
    the rows the caller can see: what those roles grant cannot be judged, so the user is beyond
    the caller, as for one user.
  Only permissions of the catalogue count. The answer's `refusedBeyondOwn` counts all three.
- **Why not loop over the users.** Calling the one-user rule per row would be N round trips over
  a selection of up to the whole list; the condition is built once from the caller's grants and
  the roles table (small) and pushed into the `UPDATE`.

## Gate

- `G2AccountTakeoverTests.Acting_on_users_chosen_by_a_search_or_filter_needs_every_permission_they_hold`
  runs `SetTakeover` (tests/Erp.Gates.Tests/G2/SetTakeover.cs). It finds every write without a
  route id whose body selects like a list (`search` or `filter`) from the running app and its API
  description, and aims each, by search and by filter and with every flag combination, at: the
  Administrator; users holding grants the caller lacks in every shape of `GrantTargets`; users
  holding one module's grants through a role in one company only (critic p03 r5, R1); and a user
  holding a role in a company the caller does not work in (the caller then works in one company
  alone). Each must stay exactly as it was, while the same requests change a user without roles.
  A set-based identity write under any other record is reported until the gate covers it.
- This is the same test p05 round 6 adds to the takeover gate (same name, aimed at workspace-wide
  roles only); p03 moves its body into `SetTakeover` so the self-tests can aim it at planted
  endpoints, and extends it to roles held per company. One set-based takeover test, not two.
- Self-test (`GateSelfTests.The_set_takeover_check_catches_all_that_match_judging_nothing_or_only_roles_held_everywhere`):
  leaky module bugs 53 (P2: no check at all) and 54 (R1: only roles held in every company). The
  check must report the Administrator changed by bug 53, and users with roles in one company and
  in a company the caller does not work in changed by bug 54, and nothing for bug 54's
  workspace-wide targets. The plants act only on the gate's own users (addresses starting `set`
  or `g2.`) and never on an empty selection, so the HTTP attack sharing the environment cannot
  deactivate the seeded administrators.
- Cost: about 30 s of the gate run and 20 s of the self-tests, inside existing environments (no new
  database or app), within the verify's processor-time maximum.

## Client address behind a proxy

The compose app now passes `ERP_KNOWN_PROXIES` / `ERP_KNOWN_NETWORKS` to the existing
`Erp:Http:KnownProxies` / `KnownNetworks` settings (nearest hop only). A deployment behind a
reverse proxy records the real client address for sign-in history, lockouts and the sign-in rate
limit. The local demo has no proxy: Docker Desktop's port forwarding shows every client as its
gateway and there is no header to trust, so the default stays empty (a client-sent
X-Forwarded-For is never trusted).
