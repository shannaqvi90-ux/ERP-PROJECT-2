# Integrity check after wave 0 (p00-foundation, p01-odoo-rig)

Date: 2026-10-03. Checker: fresh integrity agent. Branch `claude/loving-lamport-kir0aw`, starting at
`2437a68`. Compose project `integrity`, ports 19100/19101 (demo) and 19110/19111 (verify stack).

## What was run

| Run | Commit | Result |
|---|---|---|
| `./erp verify`, fresh clone | `2437a68` (start) | rc=0, 163 s: 129 .NET, 19 web unit, 9 e2e, 67 harness tests; ratchet passed |
| `./erp up`, fresh clone | `2437a68` | rc=0, 25 s; sign-in as `admin@alnoor.example` 200; 101,008 users seeded (100,004 in the demo tenant) |
| `./erp verify`, fresh clone | `30747fe` | **rc=1**: G1 HTTP isolation reported 410 "leaks", all the marker `Abdullah Siddiqui` in tenant B's own `GET /api/identity/users` (see finding 3) |
| `./erp verify`, fresh clone | `3506323` | rc=0, 178 s: 134 .NET, 19 web unit, 11 e2e, 67 harness tests; ratchet passed |
| `./erp up`, fresh clone | `3506323` | rc=0, 24 s; roles list `{ items, total: 2 }`, users total 100,004, admin menu `/identity/users`, `/identity/roles`, `/tenancy/tenant` |

All `integrity` containers, volumes and the `erp-app:integrity` image were removed afterwards.

## Shared mechanisms checked

| Mechanism | Finding |
|---|---|
| Tenancy | One mechanism: `ErpDbSession` binds the transaction-local `app.tenant_id` and marker; `ModuleDbContext` adds the query filter. No module opens its own connection or calls `set_config`. The only `app.tenant_id` reads outside the kernel are the reviewed unbound-only guards in `IdentitySql.cs`. Consistent. |
| Permissions | All 10 keys follow `module.resource.action` (`PermissionDefinition.Parse`), each endpoint declares exactly one (host refuses otherwise), labels in both languages. Consistent. |
| Audit | One path: the `audit.capture()` trigger attached by `ProtectTenantTable`. No code writes `audit.entries` directly. Consistent. |
| Money | One type, `Erp.Kernel.Money.Money`; no other `decimal` money in code yet. Consistent. |
| Lists | **Inconsistent (fixed)**: users returned a page `{ items, total }`, roles a bare array, though both are registered lists "served in pages". See finding 1. |
| Module registration | One path: `ErpModule.Register` plus one line in `ErpModules.cs`; web screens and strings by `import.meta.glob`. Nothing checked that the server menu and the web screens agree. See finding 2. |
| Strings | Server and web: English and Arabic key sets identical, keys prefixed by module, plurals complete (existing gates). Every menu, list and permission label present. |
| API document | Every endpoint in OpenAPI with summary, operation id and permission (existing gate). |
| Seeding | Demo volume and the shared comparison dataset both go through `ITenantSeeder` and `BulkInsert`. Consistent. |
| Harness (p01) | `gauntlet/compare` defaults (`http://localhost:8080`, demo sign-ins) match `./erp`. The ours sign-in driver already reads either list shape. |

## Findings and fixes

1. **Two list response shapes** (fixed, `30747fe`). `GET /api/identity/roles` returned `RoleDto[]` while
   `GET /api/identity/users` returned `{ items, total }`; `ListDefinition` and the lists decision say the
   endpoint "returns pages of rows". The list framework (p05), import/export (p14) and custom fields
   (p09) could not read both the same way. Roles now return `RolePage { items, total }` (all roles in
   one page); the web roles screen and the tests that read the roles list follow. New gate
   `RegistrationGateTests.Every_registered_list_is_served_as_a_page_whose_rows_carry_its_columns`
   checks from the OpenAPI document that every registered list returns `items` (array) and `total`
   (integer), that the rows carry every registered column, and that the search parameter is
   documented when search fields are registered. Planted check: with the old bare-array roles
   endpoint the gate fails ("must return a page { items, total }").
2. **Navigation and screens were not checked against each other** (fixed, `30747fe`, `3506323`).
   Server menu entries (`MenuEntry`) and web screens (`routes.tsx`) are registered separately; nothing
   caught a menu entry without a screen, a screen nobody can navigate to, or a screen guarded by a
   different permission than its menu entry. New gate
   `RegistrationGateTests.Navigation_reaches_every_screen_and_every_menu_entry_opens_a_screen_with_its_permission`
   also keeps menu keys, label keys and paths in their module's namespace and checks screen title keys.
   Planted check: changing the tenant screen's permission and adding an unlinked screen both fail it.
   New e2e `navigation.spec.ts`: an administrator opens every main-navigation entry in English and in
   Arabic (right to left) and each screen shows its data with no error or no-access message.
3. **G1 HTTP isolation failed at random on the product as built** (fixed, `3506323`). The reverse check
   (tenant B's responses judged for tenant A's markers) used A's generated demo names as markers. Demo
   names come from a pool of about 2,000 combinations seeded by the tenant code, and tenant B's code
   holds a random canary, so B sometimes draws the same name; B's own user `Abdullah Siddiqui CNRY…`
   then contains A's marker `Abdullah Siddiqui` and B's own users list counted as 410 leaks. The
   verify at `30747fe` failed this way; the identical code passed at `2437a68`. Markers are matched as
   substrings, so `VictimValues` now drops a marker that occurs anywhere in the other tenant's rows
   (exact duplicates were already dropped). Ids, canaries and every attack value are unchanged; a real
   leak of such a record still carries its id.
4. **Licence gate read three named lockfiles** (fixed, `30747fe`). A new npm project elsewhere would
   not have been checked. It now checks every `package-lock.json` in the repository outside
   `node_modules` and `gauntlet/evidence/`, and still requires the three known ones.
5. **Tenancy module had no module tests** (fixed, `30747fe`). Only the generic gates touched
   `/api/tenancy/tenant`. New `tests/Erp.Modules.Tenancy.Tests`: each user reads their own workspace;
   renaming validates both names and the version, trims, refuses a stale version with 409 and leaves
   the other workspace alone; viewer cannot rename, a user with no roles cannot read.
6. **Decision records and README out of step with the code** (fixed, `30747fe`). The modular-monolith
   decision said endpoints live under `/api/<module>`, but identity also maps `/api/auth`; the lists
   decision did not state the page shape; the dependency record said "both lockfiles" (there are three)
   and did not list `playwright-core` (harness, Apache-2.0); the README's "Adding a module" did not say
   that a screen must match its menu entry. All updated.

Ratchet (only raised): `rules.menuEntriesChecked` 3, `rules.listsChecked` 2, `rules.npmLockfilesChecked` 3
(new); `suite.dotnetTests` 129 → 134; `suite.e2eTests` 9 → 11. Along the integration branch's
first-parent history no minimum ever went down and no maximum went up.

## Not fixed (for the lead)

- **List registrations claim sorting and filtering the endpoints do not offer.** `identity.users`
  registers sortable `displayName`, `email`, `lastSignInAt`, `createdAt` and filterable `language`,
  `isActive`; `identity.roles` registers sortable `nameEn`, `userCount` and filterable `isSystem`. Neither
  endpoint accepts a sort or filter parameter (users always sort newest first). The query contract is
  p05's to define; the gate should then require it.
- **Operation ids follow no single pattern.** Most are `module.resource.action`
  (`identity.users.list`), but `auth.signIn`, `auth.signOut`, `auth.session`, `platform.health` and
  `identity.me.preferences` (permission `identity.profile.update`) do not. No decision states a rule;
  p15 should settle it before clients depend on the ids.
- **p01 round 2 gap still open** (piece BLOCKED): closure-captured state is not inventoried and only the
  body and Location header are judged for leaks. It belongs to p01's next round, not to this check.
- `docs/compliance/` does not exist yet; no statutory rule is implemented yet, so nothing is missing.
  The money rounding default is flagged in its decision record as needing a sourced rule before VAT.
