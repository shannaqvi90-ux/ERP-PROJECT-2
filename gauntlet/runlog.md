# Lead run log

Operational events in the gauntlet run: waves launched, interruptions, relaunches. Verdicts
live in `ledger.md`; this file only explains the run around them.

| When (UTC) | Event |
|---|---|
| 2026-10-02 21:12 | Wave 0 launched: p00 foundation and p01 Odoo rig, two rounds each, then an integrity check. Orchestration script: `gauntlet/tools/gauntlet-wave.workflow.js`. |
| 2026-10-02 ~22:10 | Container restarted mid-round. Both round-1 builders were cut off before committing; no verdicts were lost because none had been given. |
| 2026-10-02 22:13 | Uncommitted builder work preserved as WIP snapshots on the piece branches (p00: b1a6ed7, 159 files; p01: 796603f). Odoo rig containers and volumes survived. |
| 2026-10-02 22:16 | Wave 0 relaunched from the snapshots. Builders now commit work in progress every 30 to 45 minutes. |
| 2026-10-03 03:30 | Wave 0 finished: p00 and p01 two rounds each, all four verdicts BLOCKED because critics planted faults the gate suite missed; the product itself showed no leak. Integrity check passed and fixed five inconsistencies (gauntlet/integrity/wave-0-a.md). |
| 2026-10-03 03:35 | The shared Odoo rig had been torn down with its volumes: its default compose project name (b-p01-odoo-rig) matched the builder cleanup rule. Lead re-ran tools/odoo-reference/up.sh to reseed it. Orchestration fixed: builders never stop the rig, no Docker prune commands, a cross-workflow lock on the integration tree, critics plant faults only in their own piece's surface and route other gate gaps to the owning piece. |
| 2026-10-03 03:45 | Wave 1 launched as two parallel workflows: 1a = p03 identity, p02 tenancy, p00 foundation (round 3); 1b = p05 list/search, p04 shell, p01 Odoo rig (round 3). Two rounds per piece; one integrity check after both finish. |
