# ERP platform core

Multi-tenant cloud ERP for UAE trading and manufacturing companies. Project rules: `CLAUDE.md`.
Architecture decisions and their reasons: `docs/decisions/`.

## Run it

Needs only Docker (with Compose v2), bash and git.

```bash
./erp up        # build, migrate, seed the demo (100,000 users) and serve it; prints URL and sign-ins
./erp verify    # build, migrate, seed and run every test: unit, integration, gates, end to end
./erp verify --clean-clone   # G3: the same from a fresh clone of HEAD, then ./erp up there
./erp down      # stop the demo (--volumes also deletes its data)
./erp tenant create --code acme --name-en "Acme LLC" --name-ar "أكمي ذ.م.م" --admin-email owner@acme.example --admin-name "Owner"
                # platform operator: provision a workspace (also: tenant suspend|activate --code …, tenant list;
                # the audit trail names the operator: --operator <name>, else ERP_OPERATOR, else user@host)
```

Ports and the compose project come from the environment, so copies run side by side:
`ERP_PROJECT`, `ERP_HTTP_PORT` (8080), `ERP_DB_PORT` (5440), `ERP_SEED_VOLUME` (100000),
`ERP_VERIFY_HTTP_PORT`/`ERP_VERIFY_DB_PORT` (+10). Behind a TLS-inspecting proxy, set
`ERP_EXTRA_CA_CERTS` to its CA bundle (picked up from `NODE_EXTRA_CA_CERTS` or `SSL_CERT_FILE`).

The demo workspace Al Noor holds four companies (Dubai, Jebel Ali free zone, Sharjah, Abu Dhabi)
with twelve branches; Gulf Steel holds two companies. Administrators work in every company, the
read-only user in the first company only. The working company and branch switcher sits in the
top bar (Alt+C).

Demo sign-ins (password `Demo-Pass-2026`): `admin@alnoor.example` (English),
`admin.ar@alnoor.example` (Arabic), `viewer@alnoor.example` (read-only),
`noaccess@alnoor.example` (no roles), `admin@gulfsteel.example` (second workspace).

New users are invited with a one-time set-up code shown once to the administrator (Users, `Alt+N` from anywhere on the screen, or `n` when no field has the focus);
they sign in with it as the password and choose their own. Failed sign-ins pause only the client
that failed, on that account; administrators see the sign-in history and can unblock. An
invitation sent to a mistyped address is corrected in the user's panel, or deleted while nobody has
signed in with it (`identity.users.delete`); anyone who has signed in stays for the audit trail and
is deactivated instead. Roles and users are managed only by someone who holds every permission
they grant; the screens offer nothing else.

## Layout

| Path | Holds |
|---|---|
| `src/Host/Erp.Host` | The one deployable; lists every module once (`ErpModules.cs`) |
| `src/Kernel/Erp.Kernel` | Shared kernel: tenancy, RLS helpers, audit, permissions, money, strings, seeding |
| `src/Modules/<Module>/` | A module (`Erp.Modules.<Module>`) and its public contracts (`….Contracts`) |
| `src/Kernel/Erp.Kernel/Lists` | The list query engine: definitions, bindings, filter language, keyset paging, grouping |
| `src/Modules/Lists` | Saved views and the per-list definition endpoints (`/api/lists/<key>/…`) |
| `web/src/kernel/lists` | The list screen every module reuses (virtualised keyboard grid, filters, views) |
| `web/src/kernel/forms` | The record form every module reuses (fields, save/discard keys, server errors, leave guard, print) |
| `src/Kernel/Erp.Kernel/Reports` | Report definitions, parameters, columns and the source contract |
| `src/Modules/Reports` | The report engine: catalogue, run and list print endpoints, PDF (Arabic shaping), CSV, XLSX |
| `web/` | React 19 + TypeScript + Vite; `src/modules/<module>/` holds each module's screens and strings |
| `tests/Erp.Gates.Tests` | Hard gates G1 (tenant isolation), G2 (permissions) and rule gates |
| `tests/Gates/` | Reviewed allowlists the gates read, and the G3 clean-clone script |
| `tests/e2e/` | Playwright end-to-end tests run against a fresh stack |
| `gauntlet/ratchet.json` | Minimum and maximum gate counts; they may only get stricter |

## Adding a module

1. `src/Modules/<Name>/Erp.Modules.<Name>` and `….Contracts` projects; a class deriving from
   `ErpModule` that registers permissions, its `DbContext`, endpoints, menu, lists, seeders and probes.
   A list is registered with its query binding,
   `module.List(ListBinding<Row>.For(new ListDefinition(…), r => r.Id).Column("key", r => r.Value)…)`,
   and its GET endpoint takes `[AsParameters] ListRequest` and returns
   `catalog.ListBinding<Row>(key).QueryAsync(...)` as a `ListPage<T>`: search, filter language, sort,
   keyset and offset paging and grouping come with it (`docs/decisions/p05-list-search-query-contract.md`),
   so does best-match-first ordering of searches with Arabic spelling variants
   (`docs/decisions/p05-list-search-relevance.md`), and `/api/lists/<key>/definition` and saved views
   appear for it automatically. A binding holds no state (keep caches off registration objects: the
   G1 gates walk them field by field and judge every list answer against the asking tenant's rows).
   A list whose rows belong to another module is registered with
   `module.List(definition, servedBy: "<other list>")` and queried through that module's contract
   (the access list over identity's users, `docs/decisions/p02-tenancy-lists-on-the-list-contract.md`).
2. Migrations in the module (`dotnet ef migrations add … --project src/Modules/<Name>/Erp.Modules.<Name>`);
   call `migrationBuilder.GrantSchemaUsage(schema)` and `migrationBuilder.ProtectTenantTable(schema, table)`
   for every table, and `migrationBuilder.ProtectCompanyTable(schema, table)` for every table whose rows
   belong to a company (`company_id`; entities implement `ICompanyOwned`). The signed-in user's companies,
   working company and branch are in `ICompanyContext`; company facts in `ICompanyDirectory`.
   A list served from the database needs a GIN `gin_trgm_ops` index on its search
   fields and a `(tenant_id, column, id)` index per sortable column (the list index gate checks both).
3. `Resources/en.json` and `ar.json` (permission and problem texts), web screens
   (`routes.tsx`: each screen's path and permission match its menu entry; a list screen is a
   `<ListView listKey=…>`) and `i18n/{en,ar}.json` under `web/src/modules/<name>/`. List endpoints
   return `{ items, total, next, groups, ranked }`. Counts are plural messages
   (`{count, plural, one {# item} other {# items}}`; Arabic needs zero, one, two, few, many, other).
4. One line in `src/Host/Erp.Host/ErpModules.cs` and one project reference in `Erp.Host.csproj`.
   Another module is used only through its `….Contracts` project (and events); a module's
   DbContext maps only its own schema, and a web module imports nothing from another web module
   (`ModuleBoundaryGateTests`).
5. A reviewed endpoint-to-permission map, `tests/Gates/endpoint-permissions/<module>.txt`: the
   module's route prefixes and one line per endpoint with its permission and why no broader one
   (G2 refuses any `/api/<module>/` route under no map; the lists module's per-list routes derive
   their permission from the list instead).
6. Printing. Every list is printable: register its rows reader with
   `module.ListRows(listKey, PageAsync)` (the same page function its GET endpoint uses) and
   `/api/reports/lists/<key>` prints exactly what the list's query selects, as PDF in English or
   Arabic, CSV or XLSX; a list that cannot be printed is named with a reason in
   `tests/Gates/unprintable-lists.txt`. A report (parameters, grouping, totals, a record's
   document) is a `ReportDefinition` with an `IReportSource` that returns rows:
   `module.Report<TSource>(definition)`; it is served at `/api/reports/run/<key>` under its own
   permission, and its labels are web string keys (`docs/decisions/p06-form-report-report-framework.md`).
   Screens edit records with the kernel form (`web/src/kernel/forms`: `useRecordForm`,
   `RecordForm`, the field components), which brings save and discard keys, server errors on their
   fields, the unsaved-changes guard, next/previous and Print (`docs/decisions/p06-form-report-form-framework.md`).
7. Optional shell contributions in `web/src/modules/<name>/extensions.ts(x)`: top-bar context
   controls (the company/branch switcher), status-line items and command palette sources, each
   with a permission (`docs/decisions/p04-shell-layout-and-extension-points.md`). Format numbers,
   amounts and dates with `useI18n().format`, never `toLocaleString`.

The gates then attack the new endpoints and tables automatically: every documented route, query
and body parameter receives the other tenant's ids, e-mails, codes and names, GETs are checked for
existence oracles, and grant fields (`roleIds`, `permissions`) are checked for escalation. Every
header, query parameter and cookie the running app reads gets the other tenant's id and code, and
every response header is judged like the body. The tenant comes only from the session: inside a
permissioned request `ErpDbSession` refuses any other tenant, and the gate traces every binding and
every `set_config`/`SET` statement to the code that sent it. Code that needs to read across tenants
(a SECURITY DEFINER function) must be listed with its callers in
`tests/Gates/security-definer-callers.txt`; code that binds a tenant, changes session settings,
switches off the tenant filter or opens its own connection is reviewed in
`tests/Gates/tenant-bypass-sources.txt`. Variables captured by endpoint lambdas count as
process-wide state (`tests/Gates/process-state-allowlist.txt`).

GET and HEAD requests (and endpoints marked `.ReadOnlyOperation()`) run in a read-only
transaction. A POST, PUT, PATCH or DELETE never declares a `*.read` permission unless it is marked
read-only or reviewed in `tests/Gates/read-permission-writes.txt`.

Behind a reverse proxy, set `Erp__Http__KnownProxies` (or `Erp__Http__KnownNetworks`) so sign-in
rate limits see the client address.
