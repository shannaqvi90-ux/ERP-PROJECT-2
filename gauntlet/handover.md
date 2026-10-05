# Handover: the run moves to the owner's PC

Written by the lead on 2026-10-05 (about 08:00 Dubai time, 04:00 UTC) when the owner moved the
gauntlet from the Claude Code cloud machine (4 CPUs, 15 GB RAM, under 10 GB disk, three
container restarts) to their own PC (AMD Ryzen 7 7700X, 8 cores / 16 threads, 32 GB RAM, about
260 GB free on D:). A new lead session on the PC reads this file first, then `CLAUDE.md`,
`gauntlet/goal.md`, `gauntlet/plan.md` and `gauntlet/runlog.md`, and carries on the loop.

## State at the move

- Integration branch: `claude/loving-lamport-kir0aw`, pull request
  https://github.com/shannaqvi90-ux/ERP-PROJECT-2/pull/1 into `main`.
- Every piece branch (`piece/p00-foundation` … `piece/p06-form-report`) is pushed to GitHub with
  the owner's approval. Unmerged work lives only there.
- The cloud workflow was stopped cleanly: the integration lock was free, no merge was in
  progress, every critic verdict so far is recorded in `gauntlet/verdicts/` and `gauntlet/ledger.md`.
- Owner items still open in `gauntlet/needs-human.md`: #1 project name in `CLAUDE.md`, #2 Odoo
  captures to `bar/reference/`, #4 money rounding rule. #3 (ISC) and #5 (OFL fonts) are approved.

| Piece | Rounds judged | Latest verdict | Work not yet integrated | Next round |
|---|---|---|---|---|
| p00 foundation | 5 | r5 LOSS: sign-in ties Odoo (first round with every hard gate holding) | r6 builder stopped mid-round: 4 commits on `piece/p00-foundation` (sign-in with a team address and Enter to advance, fewer round trips binding the session) plus a WIP snapshot; verify profiling unfinished | 6, resume |
| p01 Odoo rig | 4 | r5 BLOCKED: a driver can act uncounted from the harness's Node process | none | 6 |
| p02 tenancy | 2 | r3 BLOCKED: company and branch grant rules guarded only by module tests (planted C3, C4, P3, U1, U2 passed the gates); Odoo wins switch-company and create-company-branch | none | 4 |
| p03 identity | 3 | r3 BLOCKED: takeover and grant gates only aim at a record that grants everything | none | 4 |
| p04 shell | 3 | r3 BLOCKED: tenant B's searches and records reach tenant A through the tab's history and cookies | none | 4 |
| p05 list/search | 1 | r1 BLOCKED (rounds 2 and 3 never integrated) | 19 commits on `piece/p05-list-search` | 4, see below |
| p06 form/report | 0 | not judged yet | 26 commits on `piece/p06-form-report` (gates, report engine with PDF/CSV/XLSX, form framework with identity and tenancy forms converged, report viewer); end-to-end tests, compare drivers, docs and a full verify still open | 1, resume |
| p07–p12, p16 | — | not started (wave 2) | — | 1 |
| p13–p15 | — | not started (wave 3) | — | 1 |

### Why p05 rounds 2 and 3 did not merge

Round 2: the integrator ran out of time inside a 30-minute verify. Round 3 failed two tests after
merging: (1) `UsersListTests` cannot find the Arabic name فاطمة because p03 round 2 (commit
77c332f, `docs/decisions/p03-identity-user-corrections.md`) moved Arabic names to `displayNameAr`
and deliberately left it out of quick search for speed at 100,000 users. The p05 builder must
reconcile with that decision without weakening the test, for example by reaching `displayNameAr`
within the search time budget. (2) A G1 run got a 500 from `DELETE /api/identity/users/{id}` when
two deletes raced; p04 fixed that race in dbd6e84, so check it is gone after merging.

## Setting up the PC (one time)

Everything runs inside Ubuntu on WSL2, because the scripts and agents expect Linux. In an
administrator PowerShell:

```powershell
wsl --update
wsl --install -d Ubuntu            # create a Linux user when asked
wsl --shutdown
wsl --manage Ubuntu --move "D:\shan projects\NEW ERP PROJECT\wsl"   # keep Ubuntu's disk on D:
```

Create `C:\Users\Administrator\.wslconfig` with:

```ini
[wsl2]
memory=24GB
processors=16
```

Then `wsl --shutdown`, open Docker Desktop → Settings → Resources → WSL integration, turn on
Ubuntu, Apply & restart. In the Ubuntu terminal:

```bash
sudo apt-get update && sudo apt-get install -y git curl unzip python3 dotnet-sdk-10.0
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash - && sudo apt-get install -y nodejs
sudo npm install -g @anthropic-ai/claude-code
git clone https://github.com/shannaqvi90-ux/ERP-PROJECT-2.git ~/ERP-PROJECT-2
cd ~/ERP-PROJECT-2 && git checkout claude/loving-lamport-kir0aw
npx -y playwright@1.63.0 install --with-deps chromium
docker info >/dev/null && ./erp up            # first run: seeded demo, prints the address
tools/odoo-reference/up.sh                    # Odoo reference with 100,000 rows per list
```

If `dotnet-sdk-10.0` is not in Ubuntu's packages, install it with Microsoft's
`dotnet-install.sh --channel 10.0` instead. Keep the clone inside Ubuntu (`~/`), not under
`/mnt/d`: Docker bind mounts from Windows paths are far slower.

Then start the lead: `cd ~/ERP-PROJECT-2 && claude` (or `claude remote-control` to follow it from
the Claude app), and tell it: "Read gauntlet/handover.md and continue the gauntlet loop."

## Restarting the loop (for the new lead)

The orchestration script is `gauntlet/tools/gauntlet-wave.workflow.js`; it now takes the machine
from its arguments. Pass on every run:

- `root`: the Linux home that holds the clone, for example `/home/<user>`; worktrees go in
  `<root>/wt/`, critic clones in `<root>/critic/`, evidence staging in `<root>/evidence-staging/`,
  the integration lock is `<root>/.integration.lock`.
- `machineNotes`: the facts agents need about the PC, replacing the cloud notes (Docker Desktop
  with WSL integration; .NET 10 SDK and Node 22 on PATH; Playwright's Chromium installed with
  `npx playwright install chromium`; 16 threads, 24 GB for WSL, plenty of disk; full internet).
- `trailer`: the commit trailer your session's instructions give.

Worktrees are recreated by the builders from the pushed branches (`git worktree add <root>/wt/<id>
piece/<id>` tracks `origin/piece/<id>`). Give each resumed piece a `resumeNote` saying what is on
its branch (the table above) and a `lastVerdict` pointing at its newest file in `gauntlet/verdicts/`.

Suggested next runs:

1. Wave 2a, resumed: p00 round 6 (finish verify speed-up first, then the sign-in tie), p06 round 1,
   p05 round 4 (the integration failure above).
2. In parallel: p02, p03, p04 round 4 and p01 round 6, each from its latest verdict; p02 also owes
   the version check on `PUT /api/tenancy/access/{userId}` and p03 roles per company and a per-user
   default company (both from `gauntlet/integrity/wave-1-a.md`).
3. Then the rest of wave 2 (p07 audit, p08 currency, p09 custom fields, p10 numbering, p11
   attachments, p12 jobs, p16 contacts), the wave-2 integrity check, and wave 3 (p13–p15).

The workflow caps concurrent agents at the CPU count minus two, which is 14 on this PC. More agents
also use up the account's usage allowance faster (two stops so far came from the usage limit, not
the machine), so start with two workflows of three or four pieces each and watch `./erp verify`
times before adding more. Record every relaunch, stop and owner decision in `gauntlet/runlog.md`.
