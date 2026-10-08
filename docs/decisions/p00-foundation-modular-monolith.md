# p00 — Modular monolith layout and module registration

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- One ASP.NET Core host (`src/Host/Erp.Host`), one shared kernel (`src/Kernel/Erp.Kernel`) and, per
  business area, two projects under `src/Modules/<Module>/`: `Erp.Modules.<Module>` (internals) and
  `Erp.Modules.<Module>.Contracts` (public records, interfaces, permission keys). A module project
  may reference the kernel and other modules' **contracts** only, never another module's internals.
- A module is a class deriving from `ErpModule`. Its `Register(ModuleBuilder)` contributes, from its
  own folder: services, permission catalogue, its `DbContext` (own schema, own migrations, own
  `__ef_migrations_history` table inside that schema), endpoints (under `/api/<module>`, or under another
  prefix the module names, as identity does for `/api/auth`), menu entries, tenant seeders, server strings (`Resources/en.json`, `Resources/ar.json`, embedded) and
  isolation probes for the G1 gate. The host lists each module once in `ErpModules.All`.
- The web app mirrors this: `web/src/modules/<module>/routes.tsx` and `i18n/{en,ar}.json` are found
  by `import.meta.glob`, so adding a module's screens needs no central edit. The server menu entry
  and the web screen describe the same place: same path (under `/<module>/`), same permission;
  every screen but home is reachable from a menu entry (`RegistrationGateTests`).
- Every module context shares one PostgreSQL connection and transaction per unit of work
  (`ErpDbSession`), so a request that touches two modules commits or rolls back as a whole while
  the modules still never read each other's tables.

## Why

- Parallel builders (p02–p16) each own a folder and a schema, so they rarely touch the same files;
  the only shared edits are one line in `ErpModules.cs` and one line in the host `.csproj`.
- Separate schemas and migration histories let each module evolve its tables independently and let
  the gates reason about "every table in every schema".
- In-process contracts keep the single deployable (CLAUDE.md stack) while making module borders
  explicit and compiler-checked.

## Rejected

- One `DbContext` for everything: every piece would edit the same model and migration chain.
- Microservices: CLAUDE.md locks a single deployable.
- MediatR for in-process messaging: versions 13+ are commercially licensed (CLAUDE.md rule 6).
  Contracts are plain interfaces; an event bus will be added in the kernel when a piece needs one.

## Integrity check, wave 1 (2026-10-04)

"Compiler-checked" held only as long as nobody added a project reference. With three modules now
using each other (tenancy reads identity's users through `IUserDirectory`, identity reads tenancy's
tenant through `ITenantDirectory`, the access list is served by identity's users list), the border
is a gate: `ModuleBoundaryGateTests` refuses a module or contracts assembly that uses another
module's implementation assembly, a module DbContext that maps a table outside the module's schema,
and a web module that imports from another web module (or a web kernel file that imports a module).
