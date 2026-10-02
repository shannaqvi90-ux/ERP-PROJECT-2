# [PROJECT NAME] — project rules

Multi-tenant cloud ERP for UAE small and mid-sized trading and manufacturing companies.
Target: do each job in scope better than Odoo does it for this market.

Claude Code reads this file at the start of every session. These rules outrank any prompt.

## Stack (locked)

- Backend: .NET 10 (LTS), ASP.NET Core, C#, EF Core with Npgsql
- Database: PostgreSQL
- Frontend: React 19, TypeScript, Vite
- Shape: modular monolith. One deployable. One module per business area. A module may use
  another module only through its public contracts and events, never its tables or internals.
- Local run: Docker Compose. Tests: xUnit with Testcontainers, Playwright for end-to-end.
- UI direction: dense, keyboard-first, Dynamics 365-style business screens.

Everything not listed here is the builders' choice. Record each choice and the reason for it
in `docs/decisions/`.

## Rules that never bend

1. **Tenant isolation.** Every tenant-owned row carries `tenant_id`. PostgreSQL row-level
   security enforces it. Application-level filters are a second layer, never the only one.
   Any cross-tenant read or write is a release blocker.
2. **Money.** Decimal types only, never floating point. Every amount is stored with its
   currency, the exchange rate used, and the base-currency amount.
3. **Ledger.** Every posted journal entry balances. Posted entries are never edited or
   deleted, only reversed. Every posting traces back to the source document that caused it.
4. **Audit.** Every business record keeps who changed what, and when.
5. **Languages.** English and Arabic (right-to-left) from the first screen, including
   printed documents.
6. **Licences.** Dependencies under MIT, Apache-2.0, BSD or the PostgreSQL licence only.
   Anything paid or copyleft needs human approval first.
7. **Odoo is a reference, not a source.** Compare against it. Never copy its code, views
   or text.
8. **Law comes from the source.** Tax, payroll and statutory rules are taken from current
   official publications (Federal Tax Authority, Ministry of Finance, MoHRE, Central Bank),
   never from memory. Each rule is recorded in `docs/compliance/` with its source link and
   the date it was checked.
9. **The bar only moves up.** Never weaken, skip or delete a test, a scenario, or a line of
   the bar to get a pass.

## Human gates

When work reaches one of these, stop that piece, write it in `gauntlet/needs-human.md`,
and carry on with other pieces. Never self-approve.

- Any tax, payroll or statutory calculation rule. The owner, a senior accountant, signs off.
- The expected results of any golden scenario in `bar/scenarios/`.
- Any new paid service, real credential, deployment, or action outside this repository.
- Any change to this file or to anything under `bar/`.

## Where things live

| Path | Holds |
|---|---|
| `bar/scenarios/` | Golden business scenarios with signed-off expected results |
| `bar/reference/` | Odoo reference captures and timings |
| `gauntlet/progress.html` | Live progress page |
| `gauntlet/ledger.md` | Every critic verdict with its evidence |
| `gauntlet/needs-human.md` | Items waiting for the owner |
| `docs/decisions/` | Architecture decisions and reasons |
| `docs/compliance/` | Statutory rules with sources and check dates |
