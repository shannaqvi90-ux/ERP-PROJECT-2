# p03-identity: the sign-in rate limit behind a shared address

Date: 2026-10-19 (round 13, wave serial-9)

## The gap (critic p03 round 12, also routed to p00)

`ErpPlatform.SignInRateLimit` allows 30 sign-in requests a minute for each remote address. In the
shipped deployment every client has the compose gateway's address, so all of them shared one
budget. Thirty anonymous requests in 3.5 s were enough for the next correct sign-ins of two
administrators, in two workspaces and from other browsers, to answer 429 `request.tooMany`. One
script stopped every user of every workspace from signing in. Without any attacker, a 40-person
company signing in at 8 a.m. would hit the same limit.

## Decision

1. **The kernel gets an additive extension point** (`Erp.Kernel.Http.RateLimitClients`). An
   endpoint may name the client a request counts as (`WithRateLimitClient`). A small middleware
   runs before the rate limiter and works out that client. It may read the body, and leaves it
   readable for the endpoint. Without that metadata nothing changes: the partition is the remote
   address, as before.
   - `RateLimitClient.Key` replaces the remote address as the partition of the endpoint's own limit.
   - `RateLimitClient.SharedCeiling`, when set, names an address many clients share. All requests
     from it together are held to a ceiling: `Erp:RateLimits:SharedAddressClients` (20 by default,
     clamped 1–1000) times the per-client limit, as the limiter's global limiter. Requests from an
     address of one client are not limited by the ceiling at all.

2. **Sign-in names its client behind a shared address** (`Auth/SignInRateLimit.cs`).
   - The address is shared when `ClientAddresses` says so: the server's gateways, loopback, a proxy
     that did not forward, or a declared shared address.
   - Each account typed is then a client of its own: the address plus the e-mail address, trimmed
     and lower-cased as sign-in does.
   - Each passkey user handle is a client of its own likewise.
   - The body is read with the endpoint's own JSON options, so the partition names exactly the
     account the endpoint will try. Case, spaces or a duplicate property give no fresh budget.
   - Bodies over 32 KB, and bodies naming neither an account nor a passkey, count against the
     address itself.
   - From an address of one client nothing changes: one budget for every account it tries.

## What this protects and what it does not

- **Guessing at one account** from the shared address stays within one client's budget (30 a
  minute), and the per-account pause still stops it after 5 failures. Set-up codes are bounded the
  same as in round 12's calculation: 30 a minute for each account.
- **A script hammering one account, or many accounts that do not exist**, uses up only those
  accounts' budgets, never another person's. The office signing in at 8 a.m. gives each person
  their own budget.
- **Spraying many accounts at once** from the shared address is held by the address's ceiling,
  600 a minute by default. This is looser than the 30 a minute the whole internet shared before.
  That old limit was itself the denial of service the critic found. A deployment that sees real
  client addresses (needs-human #15) is unaffected: there, every client has its own address and
  its own 30.
- **A flood** above the ceiling still refuses everyone behind that address for the rest of the
  minute. It now takes 20 times the requests the critic needed, and a flood is a network-level
  attack the deployment's proxy should stop. The ceiling also bounds the password-hashing work
  one address can cause.

## Not changed

- The per-client limit (`Erp:RateLimits:SignInPerMinute`, 30) is not raised.
- The comparison harness's pacing (`gauntlet/compare/lib/sign-in-limit.mjs`) is unchanged and
  still correct: it stays under 30 a minute for one client.

## Gates

`RateLimitTests.Behind_a_shared_address_each_account_has_a_clients_budget_and_everyone_together_the_shared_ceiling`
checks the following, with a per-client limit of 3 and a shared ceiling of 2 clients:

- one account's budget behind the shared address is spent after 3 requests, including when the
  same address is written in other case and with spaces;
- a colleague behind the same address still gets their own answer;
- spraying fresh accounts meets the shared ceiling, after which every account behind the address
  gets 429;
- an address of one client keeps one budget across accounts, as before.

The existing test of the per-address limit (no proxy, one budget) still passes unchanged.
