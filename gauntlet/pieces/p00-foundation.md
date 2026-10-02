# p00 — Foundation and hard gates

Everything later pieces plug into, plus the owner's hard gates as automated checks. Write the
gate checks first; they must run (and fail meaningfully, or pass vacuously only where a
ratchet minimum says so) before product code exists.

## Must exist

- **Modular monolith layout.** One ASP.NET Core host. One project per module under
  `src/Modules/<Module>` with a separate public contracts project; modules talk only through
  contracts and in-process events. Shared kernel for tenancy, permissions, audit, money,
  module registration. Each module owns its own PostgreSQL schema, its own `DbContext` and
  its own migrations, so pieces built in parallel rarely touch the same files.
- **Module registration.** A module contributes, from its own folder only: endpoints,
  permission catalogue, EF model and migrations, seed data (including bulk demo volume),
  menu entries, English and Arabic strings, list/search registrations and attack vectors
  for the tenant-isolation gate. Adding a module must not require editing a central list
  beyond one registration line.
- **Tenancy.** Every tenant-owned table has `tenant_id NOT NULL`. PostgreSQL row-level
  security is enabled and forced on every such table, keyed to a per-transaction setting.
  The application connects as a role that is not superuser, not table owner and has no
  BYPASSRLS; migrations run as a separate owner role. EF Core global query filters are the
  second layer. The tenant comes only from the authenticated session, never from a header,
  query string or body the client controls.
- **Authentication.** Email and password sign-in (strong hashing, lockout), secure session
  (cookie or token, builder's choice, recorded in `docs/decisions/`), sign-out.
- **Authorisation.** Fallback policy denies. Every endpoint declares one permission
  (`module.resource.action`) or is on `tests/Gates/anonymous-allowlist.txt`. Roles grant
  permissions. The front end hides what the user cannot do, the API enforces it.
- **Audit plumbing.** Every insert, update and delete of a business record writes who, what
  (field-level old and new), when and which tenant, in the same transaction. p07 builds the
  screens on top.
- **Money.** A value type holding decimal amount, currency, exchange rate and base-currency
  amount; `numeric` columns only.
- **API.** OpenAPI document generated from the running app covering every endpoint.
- **Front end.** React 19, TypeScript, Vite. Sign-in screen and an empty shell, English and
  Arabic with right-to-left from the first screen, strings in per-module resource files.
- **One command.** From a clean clone with only Docker, bash and git installed:
  `./erp up` builds, migrates, seeds the demo and serves it, printing the URL and demo
  sign-ins; `./erp verify` builds, migrates, seeds and runs every test (unit, integration
  with Testcontainers, gates, Playwright end-to-end). Ports and compose project name can be
  overridden from the environment so several copies run side by side.
- **Bulk seeding.** Seeding can load 100,000+ rows per list quickly (COPY or batched), so the
  demo carries Odoo-comparable volume.
- **Decisions.** Every choice and its reason in `docs/decisions/`.

## Hard gates to write first (see plan.md)

G1 tenant isolation (database and HTTP, enumerated from the running app, canary data,
before/after checksums of tenant B), G2 permissions (every endpoint, deny-by-default, grant
exactly one), G3 clean clone (script that clones HEAD to a temp dir and runs the one
command), rule gates (licence allowlist, no float money, English/Arabic string parity, audit
coverage) and `gauntlet/ratchet.json` minimum counts that the gate suite checks against itself.

## Compared against Odoo

Sign in to an empty workspace (steps, keystrokes, seconds).
