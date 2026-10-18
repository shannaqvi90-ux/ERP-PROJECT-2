# p03-identity: a set-up code still signs in from a shared address; the history names shared addresses

Date: 2026-10-18 (round 12, wave serial-9)

## The gap (critic p03 round 11)

In the one-command deployment the application sees every request from the compose network's
gateway (172.21.0.1 or 172.22.0.1). A browser that signed in to an account before is told apart
by its device cookie (`TrustedDevices`), but a fresh browser is not. Five anonymous wrong
passwords for an e-mail address therefore paused every fresh browser on that account for 15
minutes, at five requests per quarter hour. A newly invited employee always signs in from a fresh
browser with a set-up code, so they could be kept out for as long as anyone kept failing. The
sign-in history and the paused-clients view showed the same gateway address for the attacker's
script and the owner's laptop, and "Allow sign-in again" let the attacker in too.

The deployment half (a reverse proxy with `ERP_KNOWN_PROXIES` in the shipped compose) is the
owner's decision, needs-human #15. This round does the in-repo half.

## Decisions

1. **Which addresses are shared** (`Auth/ClientAddresses.cs`). An address is *shared* when:
   - it is a gateway in the server's own routing table (`/proc/net/route`, `/proc/net/ipv6_route`),
     read once at start. In a container every client reaching a published port arrives from it.
     `Erp:Auth:DetectGateways=false` turns this off;
   - it is loopback (a proxy or development server on the same machine);
   - it is a configured proxy (`Erp:Http:KnownProxies`, `Erp:Http:KnownNetworks`) that reached the
     app without a forwarded client address;
   - the deployment declares it in `Erp:Auth:SharedAddresses` (comma-separated addresses or CIDR
     networks, such as an office NAT address).

   Everything else is one client's address. The classifier is a singleton holding immutable
   arrays, fixed at start and the same for every tenant. The G1 process-state inventory passes.

2. **A valid set-up code gets past a pause on a shared address, and nothing else does**
   (migration `20261018090000_SetupCodeFromSharedAddress`). The reviewed
   `identity.verify_sign_in` gains `p_shared boolean DEFAULT false`. The app passes true only
   when the client's address is shared and the browser is not a known device of the account.
   While that client is paused on the account, an attempt still matches when all of these hold:
   - the account is active;
   - its credential has an expiry, which only generated set-up codes have (temporary passwords
     have none);
   - the expiry has not passed;
   - the proof equals the stored hash.

   Every other attempt from a paused client answers exactly as before, the same `throttled` 429,
   including a wrong code, an expired code, a temporary password and an ordinary password.
   - The 7-argument calls in the gate tests still work through the default.
   - The security-definer allowlist names the new signature.
   - `G1ReviewedLookupTests` also checks that the function answers nothing with `p_shared = true`
     on a tenant-bound connection.

3. **A success from a shared address does not clear anyone else's failures.** A sign-in from a
   shared address with no device cookie is recorded under the device id the browser is given
   (`TrustedDevices.Remember` now returns it). Before this, a success from the gateway reset the
   gateway's count on the account, and that gave the attacker five fresh guesses. Successes from an
   address of one client are recorded as before.

4. **The history says what each address is.**
   - Each attempt in `GET /api/identity/users/{id}/sign-ins` carries `addressKind` (`client`,
     `gateway`, `proxy`, `local`). An attempt from a known device also carries `device`, a stable
     six-character tag of that browser.
   - Each paused client carries `kind` (`device`, `client` or a shared kind).
   - The Sign-in history tab shows "172.21.0.1 · shared gateway · known device AbC123". A tooltip
     says why the address does not tell people apart.
   - When a shared address is paused, the tab says that "Allow sign-in again" lets everyone behind
     it try again, and that a new set-up code (Reset password…) lets one person in.
   - Every string is in English and Arabic.

## Why the brute-force protection is no weaker

- Passwords, temporary passwords and every client with an address of its own are paused exactly
  as before. Only a generated set-up code from a shared address gets past a pause.
- A set-up code is 12 random characters from 32 symbols, about 59 bits. It works for at most 168
  hours.
- Clients behind a shared address are not told apart, by the pause or by the sign-in rate limit
  (30 requests a minute for each remote address, `ErpPlatform.SignInRateLimit`). All of them
  together get at most 30 × 60 × 168 = 302,400 guesses during the code's life, a chance of about
  5 × 10⁻¹³. That is far less than five password guesses every 15 minutes against a chosen
  password.
- An attacker learns nothing new: without the code every answer is the same 429 as before.

## Alternatives tried and not taken

- **Let a set-up code past every pause, shared or not.** Rejected. For a client with an address of
  its own, the pause limits guesses to 20 an hour, against 1,800 an hour under the rate limit
  alone. That would weaken the guard for no gain, because such a client's pause never touches
  anyone else.
- **A second, larger failure budget for set-up codes.** Rejected. Any budget shared by everyone
  behind the gateway can be used up by the attacker, which brings the lockout back at a higher
  request count.
- **Trusting X-Forwarded-For by default.** Rejected: the critic rightly confirmed that it is
  ignored, because without a known proxy any client could choose its own address.

## Other findings closed this round

- **Users list `roleIds` order** (routed from p00's r11 critic). `RolesOf` sorts each user's role
  ids, so two identical requests answer identically and the G1 differential oracle sees no
  difference.
- **"Roles in one company" no longer preselects a role.** Before, the first role by name, often
  Administrator, was preselected. Now the role box reads "Choose a role", and Add (and Enter)
  grant nothing until the person picks one.
- **Enter in the reset choice resets.** Enter on the focused method radio, in the temporary
  password field or on the passkeys box submits the reset. The palette path is now palette, Enter
  on "Reset password…", then Enter, with no Tab.

## Not done this round

- **Column headers with a record open** ("Langu…") and the Roles column running past the edge.
  Column widths are set in the shared list framework (`web/src/kernel/lists/ListView.tsx`,
  `headerMinimum`). That framework is another piece's, and other builders change it in parallel,
  so it was not changed here.
- **Filtering the users list by role.** This needs the list framework to support set-valued
  columns. It also needs the framework to reject unknown query parameters (`?role=` is ignored).
  Both belong to the list framework's piece. The "Users by role" report answers who holds a role.

## Processor time this round adds

- **.NET:** about 3 s for the new gate test (it runs in the existing throttle fixture, about 60
  sign-in requests), under 0.2 s for three classifier unit tests, and one extra query in
  `G1ReviewedLookupTests`.
- **Web:** two screen tests and one more palette assertion, under 1 s.
- **e2e:** one API call in the existing pause test.
- **Total:** about 5 s of processor time, against a maximum of 10,500 s.
