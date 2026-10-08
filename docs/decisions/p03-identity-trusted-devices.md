# p03 identity: a browser that signed in before is a client of its own

Date: 2026-10-05. Piece: p03-identity, round 4. Status: accepted.

## Context

Failures pause one client on one account (`p03-identity-sign-in-throttling.md`), the client being
the network address. Behind Docker every browser arrives from the compose gateway (critics p03
rounds 1-3): one person's failures paused the account for everyone behind the same address, as
they would behind an office NAT or a shared proxy. `Erp:Http:KnownProxies` helps only behind a
proxy that names the client, which a default deployment does not have.

## Decision

OWASP's device-cookie defence: after a successful sign-in the browser receives `erp_device`
(HttpOnly, SameSite=Strict, path `/api/auth`, 180 days), an HMAC-SHA256-signed list of up to ten
(account, device id) pairs, the account being 12 bytes of the SHA-256 of its normalised address
(no address in the cookie). On the next attempt at that address from that browser, failures are
counted under `device:<id>` instead of the network address. So:

- someone behind the same address who keeps failing pauses the address, not the owner's browser;
- a device earned on one's own account gives no fresh count on anyone else's (the pair is bound
  to the account it was earned on);
- a cookie that does not verify is ignored (still the address);
- the owner's own failures still pause the owner's device.

The key comes from `Erp:Auth:DeviceKey` when set (several instances), else a random key per
process (devices fall back to their address after a restart). The sign-in history shows such a
paused client as "A browser that signed in to this account before".

Gate: `G1SignInThrottleTests.A_browser_that_signed_in_before_is_not_paused_by_failures_from_its_network_address`
(fails when the device source is ignored).
