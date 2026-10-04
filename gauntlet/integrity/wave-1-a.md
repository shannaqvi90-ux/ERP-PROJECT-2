# Integrity check after wave 1 (p02-tenancy, p03-identity, p04-shell, p05-list-search)

Date: 2026-10-04. Checker: fresh integrity agent. Branch `claude/loving-lamport-kir0aw`, starting at
`8f719c7`. Compose project `integrity`, ports 19100/19101 (demo) and 19110/19111 (verify stack).

## What was run

| Run | Commit | Result |
|---|---|---|
| `./erp verify`, fresh clone | `8f719c7` (start) | Not completed: the wrapper was stopped by this agent's 1-hour job limit while stage 1 was still running (the machine's load average was 8-9 on 4 CPUs; `G1HttpIsolationTests` alone took 36 min 47 s, `G1CompanyIsolationTests` 10 min 43 s). Every test reported up to then passed; the run's output folder was removed by verify's own clean-up, so no totals. |
| `./erp verify`, fresh clone | `b225237` (fixes) | rc=0, 4,503 s: 339 .NET (103 gate tests in 1.1 h), 159 web unit, 54 end-to-end, 146 comparison-harness tests; string and accessibility checks; ratchet passed |
| `./erp up`, fresh clone | `b225237` | rc=0, 52 s. Signed in as `admin@alnoor.example` (200): menu users, roles, company access, companies, branches, workspace, my account; users 100,004 and company access 100,004 rows, roles 2, companies 4, branches 12, every list answering `{ items, total, next, groups }`; word search over 100,004 users 0.29 s wall clock; the permission catalogue gives every resource a label, in Arabic for `admin.ar@alnoor.example`; `/tenancy/companies` serves the app (200). |

All `integrity` containers, volumes and the `erp-app:integrity*` images were removed afterwards.

## Shared mechanisms checked

| Mechanism | Finding |
|---|---|
| Tenancy | One mechanism: `ErpDbSession` binds `app.tenant_id` and, since p02, the company scope (`BindCompaniesAsync`, once per unit of work) through the one `ISessionScopeBinder` (tenancy's `CompanyScopeBinder`). Company-owned tables use `ProtectCompanyTable`; entities use `ICompanyOwned` and the kernel's named `company` query filter. The only `app.tenant_id` / `set_config` uses outside the kernel are migrations and identity's reviewed pre-tenant sign-in function. Consistent. |
| Permissions | All 24 keys follow `module.resource.action` (camel-case parts: `identity.users.resetPassword`, `identity.signIns.read`), declared from each module's contracts, one per endpoint. **Inconsistent (fixed)**: only identity had resource labels for the role matrix (finding 2). |
| Audit | One path: the `audit.capture()` trigger. Module migrations only re-attach it with column exclusions or lift FORCE inside a migration transaction for backfills. No code writes `audit.entries`. Consistent. |
| Money | One type, `Erp.Kernel.Money.Money`; no amounts in wave 1 (companies store a base currency code only). Consistent. |
| Lists | Every list screen (users, roles, companies, branches, company access) is the kernel `<ListView>`; every registered list is served by a `ListBinding` (roles in memory, access served by identity's users list through `IUserDirectory`) and answers `{ items, total, next, groups }`. **Inconsistent (fixed)**: tenancy still carried an unused pre-ListView selection and grid-key mechanism (`?id=`/`?new=1`, `gridKeys`), removed in finding 1. |
| Forms | No shared form framework yet (p06, wave 2). **Inconsistent (fixed)**: the save keys differed between modules (finding 1). Remaining divergence noted below. |
| Module registration | One path (`ErpModule.Register`, one line in `ErpModules.cs`; web routes, strings and shell extensions by `import.meta.glob`). Modules use each other only through contracts (tenancy to identity: `IUserDirectory`, the access list `servedBy` identity's users list; identity to tenancy: `ITenantDirectory`); no SQL names another module's schema; no web module imports another. **Not gated until now** (finding 3). |
| Strings | Server and web: English and Arabic key sets identical (gates), every literal web string key used in code exists (`web/scripts/check-strings.mjs`), and every dynamically built key family (emirates, months, weekdays, outcomes, list operators, view groups, tabs, actions) has both languages (checked by hand for this report). |
| Navigation | Every menu entry opens a screen with its permission and every screen has a menu entry (`RegistrationGateTests`); the e2e navigation walk opens every entry in English and Arabic. Menu: users 800, roles 810, company access 820, companies 830, branches 840, workspace 900, my account 990. |
| API document | Every endpoint in OpenAPI with summary, operation id and permission (gate). Every `/api/identity`, `/api/auth` and `/api/tenancy` endpoint is in a reviewed permission map; every `/api/lists/<list>/…` endpoint derives its permission from its list (G2). The only endpoint outside both is the anonymous `/api/health`. |
| Gate coverage | G1 (HTTP attack, company attack, database, process state, client state) enumerates endpoints and tables from the running app, so all three modules are covered without registration. Each module has its own test project (`Erp.Modules.{Identity,Tenancy,Lists}.Tests`) and e2e spec. |
| Ratchet | Along the first-parent history of `gauntlet/ratchet.json` no minimum went down, no maximum went up, no key disappeared (checked commit by commit). |

## Findings and fixes

1. **Two save-key sets in the forms** (fixed, `0c212be`). Identity's forms saved on Ctrl+Enter or
   Ctrl+S (their decision: "in every form"); tenancy's company, branch, access and workspace forms
   only on Ctrl+S. Identity matched S with `event.key`, so Ctrl+S did nothing on an Arabic keyboard
   layout, contrary to the shell's rule that shortcuts match the key's position
   (`p04-shell-keyboard.md`). Tenancy now registers Mod+Enter as well (through the shell registry,
   so it is in the shortcut sheet), identity matches S by `KeyboardEvent.code` and ignores AltGr,
   and save buttons announce both keys. Tenancy's unused `readSelection`/`writeSelection`/`gridKeys`
   (a second, dead selection-in-address and grid-key mechanism next to the list framework's
   `?open=`) were removed. New web unit tests: Ctrl+Enter saves a new company; `formKeys` saves on
   Ctrl+Enter, Ctrl+S and Arabic-layout Ctrl+S, never on a typed "s" or AltGr. Both fail on the old code.
2. **Permission matrix rows without labels for two modules** (fixed, `bb4c4a6`). The roles screen
   heads each matrix row with `resource.<module>.<resource>`; identity defined its four, tenancy
   (5 resources) and lists (1) none, so those rows fell back to one permission's label ("View
   companies" as a row heading). Labels added in English and Arabic; `StringGateTests` now requires
   a resource label for every permission's resource.
3. **Module borders were not checked** (fixed, `b225237`). CLAUDE.md allows one module to use
   another only through contracts and events. With three modules now calling each other, nothing
   but the current project references kept that. New `ModuleBoundaryGateTests`: no module or
   contracts assembly uses another module's implementation assembly; every module DbContext maps
   only its own schema; no web module imports another web module and the web kernel imports no
   module. A self-test plants each case (the gate test assembly as a module, cross-module and
   kernel-to-module imports) and checks contracts, kernel and own-module imports pass.
4. **Decision records out of step with the code** (fixed, `0c212be`, `b225237`).
   `p03-identity-roles-per-company.md` and `p03-identity-user-administration.md` still said p02's
   companies and `ICompanyContext` were not on the integration branch; `p03-identity-screens.md`
   named a `?user=<id>` address (the code uses the list framework's `?open=`) and a table "p05 can
   replace later" (it has); `p00-foundation-lists.md` still described the page as `{ items, total }`
   with skip/take and roles "as one page" (superseded by p05's contract and `.InMemory`). Each now
   carries a dated wave 1 note. README's adding-a-module steps now name the boundary gate and the
   reviewed endpoint-permission map that G2 requires for every module route.

Ratchet (only raised): `rules.moduleBoundariesChecked` 63 (new), `rules.serverStrings` 111 → 125,
`rules.webStrings` 483 → 503, `suite.dotnetTests` 337 → 339, `suite.webUnitTests` 157 → 159 (the counts of the passing verify above).

## Not fixed (for the lead)

- **Roles per company and a per-user default company (p03 scope) are still not built**, now that
  p02 is integrated. `identity.user_roles` has no `company_id`; every role applies in the whole
  workspace; identity reads no `ICompanyContext`. The p03 decision planned exactly this once p02
  landed. It changes every request's permission set and needs its own G2 case, so it is p03's next
  round, not an integrity fix. The p03 decision also says an administrator chooses a user's
  default (working) company; no endpoint lets an administrator set another user's working company
  (only the user switches their own, `PUT /api/tenancy/workplace`).
- **`PUT /api/tenancy/access/{userId}` carries no version.** Every other update in identity, tenancy
  and lists sends the version read and gets 409 when stale; two administrators editing one user's
  company access overwrite each other silently. Changing the body touches G1/G2 body attacks and
  the access screen, so it belongs to p02.
- **Form plumbing still differs until p06**: identity's forms handle keys on the form element and
  map API field errors with their own `fieldErrors`, tenancy's register shortcuts with the shell
  and use `problemOf`/`Field`; identity's save keys are not in the shortcut sheet. The keys a user
  presses are now the same; the shared form framework (p06) should absorb both.
- **New-record address differs**: users and roles open the new form at `?new`, companies and
  branches at `?open=new`. Both are documented in their decisions and used by the compare drivers;
  p06 should pick one.
- **Operation ids** (open since wave 0, p15): `auth.signIn`, `platform.health`,
  `identity.me.preferences` and the per-list `<list key>.views.*` do not follow `module.resource.action`.
