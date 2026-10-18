# p00 round 12: the permission gate names the caller's own ids; Money cannot be forged

Date: 2026-10-18. Piece: p00-foundation. Status: accepted.

## 1. G2 tries the caller's own ids (critic p00 round 11, biggest gap)

Every G2 matrix built its requests with random route ids and an empty body, so a permission check
that depends on route values was never tried with the values that matter: the caller's own. The
critic's plant P6 ("people may always manage their own record": a route `id` or `userId` equal to
the caller's user id passes) let a user with no roles read and change their own user record while
every gate and module test passed.

Decision:

- `OwnIds.OfAsync` (tests/Erp.Gates.Tests/G2/OwnIds.cs) reads, as the database superuser and never
  from the application, the ids that belong to the caller: their user id, their tenant, the session
  the requests run in (found by the hash of its token, so a renamed sessions table fails the gate
  instead of leaving the session untried), and every uuid held by the latest six rows of every
  tenant table that mention the caller (`t::text LIKE '%<user id>%'`). That is generic on purpose:
  their role and company assignments, companies and branches, sessions, sign-ins, passkeys, saved
  views and the audit entries about them are found today, and the rows of self-service tables to
  come (payslips, expense claims, leave) are found from the day the table exists.
- The deny-by-default matrix (`CarrierMatrix.DeniedEverythingAsync`) sends, after its random-id
  requests, one request per routed permissioned endpoint and own id (every route parameter set to
  that id, the id also in the query string and, for a method with a body, in `{"id","userId"}`),
  plus one request per routeless endpoint naming the caller's user id. It does this through every
  carrier, each carrier with a session of its own (a planted bypass that ends the session then
  spoils only that carrier's later requests). Every answer must be 403. Reads go first and the
  endpoints that end sessions or remove records last.
- The grant-exactly-one matrix does the same for every endpoint of the held permission's module
  that declares another permission (holding `identity.users.read` must not open
  `PUT /api/identity/users/{me}`), through one carrier per permission, the carriers taken in turn,
  so both carriers are covered across the matrix. Endpoints of other modules keep their random-id
  requests through every carrier. Why the module limit: a shortcut of the form "holding something
  in this area lets me act on my own record" is the realistic one, and all modules' endpoints for
  every permission would add about 30,000 requests to a verify that sits near its processor-time
  maximum.
- A new test, `A_user_who_lost_every_role_is_denied_everything_on_the_records_they_made`: a user
  holding every list's permission works in both gate companies (one through a named branch), saves
  a personal view in every list and loses every role; the deny matrix must then hold with their
  saved views, companies and branch among the own ids (the test asserts they are).
- Self-test `The_matrices_catch_a_permission_check_that_passes_requests_naming_the_callers_own_ids`
  hosts `SelfBypassModule` in three modes: `user` (P6 itself), `session` (a route value equal to the
  caller's session id) and `holder` (P6 only for callers holding a permission of the endpoint's
  module, which only the grant-one matrix can see). Each must be reported, only on the own-id
  requests; for `holder` the deny matrix must stay clean. The carrier self-test (round 9, P3) runs
  without the own-id requests, as before.
- The critic's plant P6 itself, applied to this branch: the deny test, the former-holder test and
  the grant-one test all fail (1,326 findings, among them
  `[bearer] GET /api/identity/users/{id:guid} naming the caller's user id answered 200`).
- Ratchet: `g2.selfReferencingDenyRequests` 900 (the no-roles user: 912 requests, 8 own ids, 108
  endpoints; the former holder: 3,000 requests, 26 own ids), `g2.ownIdsTried` 8 and
  `g2.selfReferencingGrantRequests` 4,900 (4,972 measured).

## 2. Money's invariants cannot be bypassed (critic p00 round 11)

`Money` had public `init` setters, so `new Money { ExchangeRate = 0, BaseAmount = 999 }`,
`line with { BaseAmount = 50 }` and a JSON body built figures the type refuses.

Decision: the properties have no setters (no object initialiser and no `with` expression compiles
against them). JSON goes through `MoneyJsonConverter`, put on the type itself, which builds every
figure with `Money.Unrounded` and refuses a lower-case or unknown currency, a rate that is not
positive or has more than eight decimals, a rate other than 1 in the base currency, an amount with
more than ten decimals or beyond the column, a base amount other than the amount times the rate
rounded once (it may be left out), a missing, unknown or repeated field, and anything that is not
an object. Writing is unchanged (the same five fields, decimals by the serializer's decimal
converter: strings under the platform's options). A figure read from JSON is unrounded (clients
state no totals). EF Core reads the columns back through the type's constructor (its parameters are
named after the properties); a database round trip test pins it, the stated flag included.
`default(Money)`, the one value no factory makes, is refused by every operation and by the JSON
writer (`IsInitialised`). The rounding behaviour is unchanged: every earlier `MoneyTests` test
passes as it was, and nothing new is decided about the law.

## 3. Smaller findings

- Role ids of a user are answered ordered by id (`RolesOf` in the identity module), so two reads
  answer the same list; the G1 differential oracle's "unexpected oracle" on the order goes away
  without the gate's normaliser being loosened. `A_users_role_ids_are_answered_in_a_fixed_order`
  pins it.
- The first administrator of a workspace made by `./erp tenant create` signs in with a temporary
  password: the first sign-in answers `auth.passwordChangeRequired` and the sign-in screen asks for
  a password of their own (the flow users made with `mustChangePassword` already use). The command
  says so. `ProvisioningTests` pins it, including that the initial password stops working. The
  Odoo comparison's sign-in task signs in a user made through the API, so its path is unchanged.

## 4. Processor time

Measured on this machine (load about 20 to 40 on 16 CPUs), the G2 permission tests' process alone,
after the build: 77 s before this round, 101 s after, so about 24 s more for the verify (about
0.25 % of the 10,500 s maximum). Nothing else this round adds measurable processor time. The
verify's own figures are in the integrator notes of this round.
