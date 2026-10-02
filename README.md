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
```

Ports and the compose project come from the environment, so copies run side by side:
`ERP_PROJECT`, `ERP_HTTP_PORT` (8080), `ERP_DB_PORT` (5440), `ERP_SEED_VOLUME` (100000),
`ERP_VERIFY_HTTP_PORT`/`ERP_VERIFY_DB_PORT` (+10). Behind a TLS-inspecting proxy, set
`ERP_EXTRA_CA_CERTS` to its CA bundle (picked up from `NODE_EXTRA_CA_CERTS` or `SSL_CERT_FILE`).

Demo sign-ins (password `Demo-Pass-2026`): `admin@alnoor.example` (English),
`admin.ar@alnoor.example` (Arabic), `viewer@alnoor.example` (read-only),
`noaccess@alnoor.example` (no roles), `admin@gulfsteel.example` (second workspace).

## Layout

| Path | Holds |
|---|---|
| `src/Host/Erp.Host` | The one deployable; lists every module once (`ErpModules.cs`) |
| `src/Kernel/Erp.Kernel` | Shared kernel: tenancy, RLS helpers, audit, permissions, money, strings, seeding |
| `src/Modules/<Module>/` | A module (`Erp.Modules.<Module>`) and its public contracts (`….Contracts`) |
| `web/` | React 19 + TypeScript + Vite; `src/modules/<module>/` holds each module's screens and strings |
| `tests/Erp.Gates.Tests` | Hard gates G1 (tenant isolation), G2 (permissions) and rule gates |
| `tests/Gates/` | Reviewed allowlists the gates read, and the G3 clean-clone script |
| `tests/e2e/` | Playwright end-to-end tests run against a fresh stack |
| `gauntlet/ratchet.json` | Minimum and maximum gate counts; they may only get stricter |

## Adding a module

1. `src/Modules/<Name>/Erp.Modules.<Name>` and `….Contracts` projects; a class deriving from
   `ErpModule` that registers permissions, its `DbContext`, endpoints, menu, seeders and probes.
2. Migrations in the module (`dotnet ef migrations add … --project src/Modules/<Name>/Erp.Modules.<Name>`);
   call `migrationBuilder.GrantSchemaUsage(schema)` and `migrationBuilder.ProtectTenantTable(schema, table)`
   for every table.
3. `Resources/en.json` and `ar.json` (permission and problem texts), web screens and
   `i18n/{en,ar}.json` under `web/src/modules/<name>/`.
4. One line in `src/Host/Erp.Host/ErpModules.cs` and one project reference in `Erp.Host.csproj`.

The gates then attack the new endpoints and tables automatically.
