# Gauntlet plan

Maintained by the lead. It splits `gauntlet/goal.md` into pieces that can be built and judged
on their own. Builders and critics both read it. It is the decomposition of the goal, not a
builder's claim about what was built.

## Machine facts (this run)

From 2026-10-05 the run continues on the owner's PC; `gauntlet/handover.md` has the current
machine and how the loop restarts there. The facts below describe the original cloud machine.


- 4 CPUs, 15 GB RAM, ~30 GB disk. At most two agents work at once.
- Docker daemon: start with `dockerd` if `docker info` fails (`(nohup dockerd >/tmp/dockerd.log 2>&1 &)`).
- .NET 10 SDK: `/opt/dotnet` (also `mcr.microsoft.com/dotnet/sdk:10.0`). `builds.dotnet.microsoft.com`
  is blocked by egress policy; NuGet, npm, Docker Hub and MCR work.
- Chromium for Playwright: `/opt/pw-browsers` (do not run `playwright install`).
- Odoo reference: Odoo Community `odoo:20.0` image, served on `http://localhost:8069` once the
  rig (piece p01) is up.

## Branches and worktrees

- Integration branch: `claude/loving-lamport-kir0aw` in `/home/user/ERP-PROJECT-2`. Only the
  integrator and the recorder write here, one at a time.
- Each builder works in its own worktree `/home/user/wt/<piece>` on branch `piece/<piece>`,
  merging the integration branch in before it starts.
- Each critic judges a fresh clone of an integration commit in `/home/user/critic/<piece>-r<round>`.
  A critic never reads `/home/user/wt/*` or any builder notes.

## Ports

Each piece owns a port block so builders and critics never collide:
`base = 20000 + 100 * piece_number`. Builders use `base .. base+49`, critics `base+50 .. base+99`.
The integration demo uses the product's default ports. Odoo uses 8069.
The one command must honour port and compose-project overrides from the environment.

## Hard gates (owner's bar, section 1)

Written as automated checks before the product is built, then only ever made stricter.
`gauntlet/ratchet.json` records minimum counts (gate tests, endpoints attacked, tables
checked). A critic fails any round in which a count went down.

- **G1 Tenant isolation.** Signed in as tenant A, an attacker tries every route, query,
  export, job and file path to reach tenant B. Covers the database (every tenant table has
  `tenant_id`, row-level security enabled and forced, the app role cannot bypass it) and the
  HTTP surface (every endpoint enumerated from the running app, called with tenant B's ids,
  tenant-switch headers, query and body fields; B's canary data must never appear and B's rows
  must never change). Exports, jobs and files register their own attack vectors; an endpoint
  family with no attack coverage fails the gate. Any leak fails the whole round.
- **G2 Permissions.** Every endpoint either declares a permission or sits on a short, reviewed
  anonymous allowlist. A user whose roles grant nothing is denied everything; granting exactly
  one permission opens exactly that action. Screens hide what the user cannot do.
- **G3 Clean clone.** One command takes a clean clone to built, migrated, seeded and all tests
  passing. One command takes a clean clone to a running, seeded demo.
- **Rule gates from CLAUDE.md** (same ratchet): licence allowlist for every NuGet and npm
  dependency; no floating-point money in schema or code; every English UI string has an Arabic
  twin and the reverse; every write to a business record leaves an audit row.

## Odoo comparison (owner's bar, section 2)

`gauntlet/compare/` holds a blind, instrumented Playwright harness. For each task it drives
both products along the shortest expert path and records steps (user actions), keystrokes,
machine seconds, modelled human seconds (keystroke-level model) and screenshots with branding
hidden. Odoo baselines live in `gauntlet/reference/odoo/` (`bar/reference/` is a human gate,
see `needs-human.md`). A tie is a loss.

Named tasks: find one record among 100,000; create a user with a restricted role; add a custom
field and filter by it; switch to Arabic; import 5,000 rows; follow an approval. Critics add a
task for every other thing a user can touch in their piece. Tasks are never removed.

## Pieces

| # | Piece | Wave | Depends on | Compared against Odoo on |
|---|---|---|---|---|
| p00 | Foundation and hard gates | 0 | — | sign in |
| p01 | Odoo reference rig and blind comparison harness | 0 | — | (instrument) |
| p02 | Tenants, companies and branches | 1 | p00 | create company and branch; switch company |
| p03 | Users, roles and permissions | 1 | p00 | create a user with a restricted role |
| p04 | English/Arabic app shell | 1 | p00 | switch to Arabic; navigate by keyboard |
| p05 | List and search framework | 1 | p00 | find one record among 100,000 |
| p06 | Form and report framework | 2 | p05 | edit and save a record; print a report in Arabic |
| p07 | Audit trail | 2 | p05 | who changed this field, and when |
| p08 | Multi-currency | 2 | p05 | add a rate; see an amount in base currency |
| p09 | Tenant-defined custom fields | 2 | p05, p06 | add a custom field and filter by it |
| p10 | Document numbering | 2 | p00 | configure a sequence |
| p11 | Attachments | 2 | p06 | attach a file and find it again |
| p12 | Background jobs | 2 | p00 | see and rerun a job |
| p13 | Approval flows | 3 | p06, p12 | follow an approval |
| p14 | Import and export | 3 | p05, p09, p12 | import 5,000 rows; export a filtered list |
| p15 | Documented API for everything | 3 | all | do a screen task through the API |
| p16 | Shared Contacts directory (owner decision, 2026-10-02) | 2 | p05, p06 | find one contact among 100,000; custom field and filter; import 5,000 |

Piece scope details are in `gauntlet/pieces/<piece>.md`.

Owner decisions (2026-10-02): Contacts is part of the platform core as shared master data and
is the main 100,000-record list for the comparisons (p16). Odoo captures stay in
`gauntlet/reference/` until the owner approves `bar/reference/`. Pull requests target `main`.

## Round protocol

1. **Build.** Builder gets the piece spec, the goal, CLAUDE.md and, on a retry, the previous
   critic's verdict. It writes or tightens the gate checks first, then builds, then runs the
   whole suite, then commits on `piece/<piece>`.
2. **Integrate.** Integrator merges the piece branch into the integration branch, keeps every
   test passing, pushes.
3. **Judge.** A new critic clones that integration commit, runs the one command, the tests, the
   app in a browser, the database, and the Odoo side by side. It writes evidence to
   `gauntlet/evidence/<piece>/r<round>/` and returns one verdict naming the single biggest gap.
4. **Record.** Recorder stores the verdict in `gauntlet/verdicts/`, appends it to
   `gauntlet/ledger.md`, rebuilds `gauntlet/progress.html` (`node gauntlet/tools/record.mjs`),
   commits and pushes.
5. The piece goes back to step 1 with the gap. There is no final round.

Verdicts: `BLOCKED` (a hard gate failed or the product does not run), `LOSS` (gates hold but
the piece loses or ties Odoo on a task, or misses part of its scope), `WIN` (gates hold and it
beats Odoo on every task compared; the critic still names the biggest remaining gap).
