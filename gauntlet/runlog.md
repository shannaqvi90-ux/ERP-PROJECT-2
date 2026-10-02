# Lead run log

Operational events in the gauntlet run: waves launched, interruptions, relaunches. Verdicts
live in `ledger.md`; this file only explains the run around them.

| When (UTC) | Event |
|---|---|
| 2026-10-02 21:12 | Wave 0 launched: p00 foundation and p01 Odoo rig, two rounds each, then an integrity check. Orchestration script: `gauntlet/tools/gauntlet-wave.workflow.js`. |
| 2026-10-02 ~22:10 | Container restarted mid-round. Both round-1 builders were cut off before committing; no verdicts were lost because none had been given. |
| 2026-10-02 22:13 | Uncommitted builder work preserved as WIP snapshots on the piece branches (p00: b1a6ed7, 159 files; p01: 796603f). Odoo rig containers and volumes survived. |
| 2026-10-02 22:16 | Wave 0 relaunched from the snapshots. Builders now commit work in progress every 30 to 45 minutes. |
