# p03 — The application never reads a password hash; sign-in proves passwords through one reviewed function

Date: 2026-10-03. Piece: p03-identity. Status: accepted.

## Context

p00's round-2 critic showed that the application role could call
`identity.resolve_login(email)` on an unbound connection and receive any tenant's `tenant_id`,
`user_id` and password hash. The HTTP surface never exposed it, but any SQL injection anywhere
in the application would have read every tenant's credential hashes. The application role could
also read `identity.users.password_hash` of its own tenant directly. The ratchet caps SECURITY
DEFINER functions at 2 (`g1.securityDefinerFunctions`), so a third reviewed function was not an
option.

## Decision

1. **Credentials live apart from users** in `identity.user_credentials` (one row per user, same
   id, `(tenant_id, id)` foreign key to the user). The application role has column privileges:
   it may INSERT, UPDATE `password_hash` and read only `id, tenant_id, must_change, expires_at,
   changed_at, changed_by`. It can set a password but can never read one back, of any tenant.
   The table has row-level security (forced), the standard policy and the audit trigger, with
   `password_hash` recorded as `[redacted]`. EF never loads the entity: it is inserted, updated
   with `ExecuteUpdate`, and queried only for the readable columns.
2. **`identity.verify_sign_in` replaces `resolve_login`** (still two SECURITY DEFINER functions
   in total). It is called twice per sign-in on the unbound connection:
   - without proofs it returns the *challenge* of each account with the address:
     `algorithm$iterations$salt`, never the hash, never an id. For an address that exists
     nowhere it returns one decoy challenge whose salt is `sha256(secret || email)`, keyed by a
     random secret in the platform table `identity.sign_in_secret` that only the function's owner
     can read. The decoy is stable, so asking twice does not tell it apart from a real account.
   - with proofs (the application's PBKDF2 of the typed password under each salt, in the stored
     format) it returns the challenge and workspace of each account whose stored hash equals a
     proof, is active, whose set-up code has not expired and whose client is not paused. Every
     other outcome is recorded in that account's sign-in history (see the throttling record).
   The application binds the matched workspace and finds the user by e-mail inside it. Knowing a
   stored hash is equivalent to knowing the password *for this function only*, and nothing can
   read stored hashes except this function.
3. **Recording failures needs the account's tenant.** The function binds the account's tenant
   (`app.tenant_id` and `app.tenant_tx`, transaction-local) for the insert into
   `identity.sign_in_attempts` only, and unbinds before moving on and before it returns, so the
   standard `tenant_isolation` policy checks every row it writes. It still answers nothing when it
   is called on a connection that is already bound. `erp_auth` (NOLOGIN) holds INSERT on that one
   table; `tests/Gates/auth-resolver-writes.txt` reviews it and the gate compares both ways.
4. The kernel's `PasswordHasher` gains `ChallengeOf`, `Prove` and `IsOutdated` (additive).
   Hashes below today's cost are renewed after a successful sign-in, as before.

## Gates added first

- G1 credential gate (`G1CredentialTests`): no column whose name says it holds a secret is
  readable by the application role unless reviewed (`tests/Gates/app-readable-credentials.txt`,
  today only the SHA-256 of session tokens); no reviewed function returns such a column or a
  whole row; the sign-in function, called as the application role, returns no hash, no tenant or
  user id before the password is proven, a decoy of the same shape for unknown addresses, and
  leaves the connection unbound; `erp_auth` writes only where reviewed.
- The reviewed-lookup gate now exercises `verify_sign_in` in both modes on a bound connection.
- The gate self-test's planted "careless lookup" now misuses `verify_sign_in` (proving the
  well-known gate password for every account with an address); the trace and leak detectors
  still have to catch it.

## Alternatives rejected

- *Verify in SQL with pgcrypto `crypt()`*: moves CPU-heavy hashing into the database and sends the
  plaintext password to it (statement logs); PBKDF2-HMAC-SHA512 is not in pgcrypto.
- *Deterministic per-e-mail salt so one call suffices*: ties the hash to the address and gives the
  same person the same hash in two workspaces.
- *A third SECURITY DEFINER function*: the ratchet maximum is 2 and may only fall.
