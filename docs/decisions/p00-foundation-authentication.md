# p00 — Authentication: opaque server sessions, PBKDF2, lockout, CSRF header

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- **Opaque session tokens**: 256 random bits (base64url, 43 chars). Only the SHA-256 hash is
  stored (`identity.sessions.token_hash`). Absolute lifetime `Erp:Auth:SessionHours` (12 h).
- Delivered as an `HttpOnly`, `SameSite=Strict` cookie `erp_session` (`Secure` on HTTPS, or always
  with `Erp:Auth:AlwaysSecureCookie`). API clients may ask for the token in the sign-in response
  (`issueToken: true`) and send `Authorization: Bearer <token>`.
- **Sign-out** revokes the session row (`revoked_at`) and clears the cookie; a revoked or expired
  token is rejected on the next request.
- **Passwords**: PBKDF2-HMAC-SHA512, 210,000 iterations, 16-byte salt (OWASP Password Storage
  Cheat Sheet), self-describing format so the cost can rise and old hashes are rehashed on sign-in.
  Minimum 10, maximum 128 characters. Built on .NET's `Rfc2898DeriveBytes.Pbkdf2`, no package.
- **Lockout**: 5 consecutive failures pause sign-in for 15 minutes (configurable). Failures are
  counted in the account's own tenant in a separate committed transaction. Unknown e-mail, wrong
  password, inactive user, locked account and suspended workspace all return the same 401 message
  and do the same hashing work (timing does not reveal which).
- **E-mail is unique per tenant**, not globally. If one address matches in several workspaces and
  the password matches more than one, the response lists only those workspaces and the client
  repeats the sign-in with `workspace`.
- **Rate limit**: sign-in is limited per client IP (`Erp:RateLimits:SignInPerMinute`, 30).
- **CSRF**: unsafe API requests must carry `X-Erp-Request` or an `Authorization` header. Neither
  can be added cross-site without a CORS preflight, which the API never grants. Together with
  `SameSite=Strict` this blocks forged requests.

## Why

- Server-side sessions can be revoked immediately (sign-out, deactivation, suspension) — the
  resolver re-checks user active and tenant active on every request. JWTs cannot be revoked
  without a server lookup anyway, and would carry the permission list in a client-held token.
- Hashing the token means a database read leak does not hand out live sessions.
- PBKDF2 ships in .NET (no dependency, FIPS-friendly). Argon2id would need a third-party package.

## Rejected

- ASP.NET Core Identity: brings its own schema (string keys, no tenant column, no RLS), and its
  tables would have to be reshaped to meet rule 1.
- JWT access tokens: no immediate revocation; larger attack surface (algorithm confusion, key
  management).

## Client address behind a proxy (2026-10-02)

The sign-in limiter partitions by client address. Behind a reverse proxy every request would come
from the proxy's address, so one client could exhaust the limit for everyone. Setting
`Erp:Http:KnownProxies` (addresses) or `Erp:Http:KnownNetworks` (CIDR) turns on forwarded-header
handling for exactly those proxies and one hop; with neither set the header is ignored, so a
client cannot choose its own address. Account lockout stays per account (5 failures, 15
minutes); anyone who knows an address can pause that account's sign-in, as on any lockout
system. Progressive delays or a challenge instead of a hard lockout are a later product
decision.
