# p03 — Sign-in failures pause one client on one account, never the account

Date: 2026-10-03. Piece: p03-identity. Status: accepted.

## Context

p00 counted failed sign-ins on the account (`users.failed_sign_in_count`) and paused the account
for 15 minutes after 5. Every account with the address was counted, in every tenant, so anyone
who knew a tenant B address could stop tenant B's user signing in. The error text also stated the
policy ("after 5 failed attempts … 15 minutes").

## Decision

- Failures are counted per **account and client source**: the IPv4 address, or the /64 network of
  an IPv6 address (one subscriber's allocation, so rotating inside it does not reset the count).
  After `Erp:Auth:LockoutThreshold` failures (default 5, clamped 3–50) within
  `Erp:Auth:LockoutMinutes` (default 15, clamped 1–1440) that client is paused on that account;
  the owner on their own client, other accounts with the same address and other tenants are
  unaffected. Counting restarts after the client's own successful sign-in there and after an
  administrator's unblock (`users.sign_in_unblocked_at`).
- Attempts are kept in `identity.sign_in_attempts`: succeeded (written with the session),
  failed, throttled (paused client, password not checked), inactive (deactivated account) and
  expired (set-up code past its time). It is the user's sign-in history (GET
  `/api/identity/users/{id}/sign-ins`, permission `identity.signIns.read`), with the clients
  paused now; POST `/users/{id}/unblock` clears pauses. The table is append-only for the
  application (INSERT and SELECT only) and is the one audit-exempt log besides `audit.entries`;
  sessions, previously exempt, are now audited (token hash redacted). The exempt count stays 2.
- Every failure answers the same 401 `auth.signInFailed` with a text that states no policy, in
  English and Arabic; a paused client cannot tell it is paused.
- Client addresses come from the connection, or from `X-Forwarded-For` only behind configured
  proxies (`Erp:Http:KnownProxies` / `KnownNetworks`, from p00).

## Trade-offs

- A distributed guesser (many /64s or IPv4 addresses) is not stopped per account; it is slowed by
  the per-address sign-in rate limit, PBKDF2-SHA512 at 210,000 iterations and the 10-character
  minimum. An account-wide ceiling would bring back the denial of service the critic found.
- People behind one office NAT share a source: an attacker in the same office can pause that
  office's client for an account; an administrator can unblock at once.
- The G1 attack signs in with tenant B's addresses and wrong passwords, which adds failed rows to
  B's sign-in history. `tests/Gates/g1-recorded-rows.txt` leaves exactly those rows
  (`outcome <> 'succeeded' AND session_id IS NULL`) out of tenant B's checksum; any other change,
  including a successful sign-in, still fails the gate. The two lockout columns it replaces were
  removed from `g1-volatile-columns.txt`.
- G1 gate `G1SignInThrottleTests` proves it: an address present in two tenants, attacked from
  one client, still signs in from other clients in both tenants; the paused answer equals the
  wrong-password answer and contains no digits in either language.
