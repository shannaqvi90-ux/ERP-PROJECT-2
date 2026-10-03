# Lead run log

Operational events in the gauntlet run: waves launched, interruptions, relaunches. Verdicts
live in `ledger.md`; this file only explains the run around them.

| When (UTC) | Event |
|---|---|
| 2026-10-02 21:12 | Wave 0 launched: p00 foundation and p01 Odoo rig, two rounds each, then an integrity check. Orchestration script: `gauntlet/tools/gauntlet-wave.workflow.js`. |
| 2026-10-02 ~22:10 | Container restarted mid-round. Both round-1 builders were cut off before committing; no verdicts were lost because none had been given. |
| 2026-10-02 22:13 | Uncommitted builder work preserved as WIP snapshots on the piece branches (p00: b1a6ed7, 159 files; p01: 796603f). Odoo rig containers and volumes survived. |
| 2026-10-02 22:16 | Wave 0 relaunched from the snapshots. Builders now commit work in progress every 30 to 45 minutes. |
| 2026-10-03 ~02:15 | Wave 0 finished: p00 and p01 two rounds each, all four verdicts BLOCKED because critics planted faults the gate suite missed; the product itself showed no leak. Integrity check passed and fixed five inconsistencies (gauntlet/integrity/wave-0-a.md). |
| 2026-10-03 02:20 | The shared Odoo rig had been torn down with its volumes: its default compose project name (b-p01-odoo-rig) matched the builder cleanup rule. Lead re-ran tools/odoo-reference/up.sh to reseed it. Orchestration fixed: builders never stop the rig, no Docker prune commands, a cross-workflow lock on the integration tree, critics plant faults only in their own piece's surface and route other gate gaps to the owning piece. |
| 2026-10-03 02:30 | Wave 1 launched as two parallel workflows: 1a = p03 identity, p02 tenancy, p00 foundation (round 3); 1b = p05 list/search, p04 shell, p01 Odoo rig (round 3). Two rounds per piece; one integrity check after both finish. |
| 2026-10-03 ~08:50 | Account usage limit reached. Both wave-1 workflows stopped: p00 round 3 was integrated (1142805) but its critic died; recorders for p03 r1 and p01 r3 died; four builders died before starting. |
| 2026-10-03 09:05 | Integrator p00 r3 was flagged for "security test removal". Checked: no test was deleted; it added seven reviewed exceptions (six personal saved-view writes under a read permission, one set_config in p03's sign-in function). Left for the next p00 critic to scrutinise. |
| 2026-10-03 09:10 | Lead recorded the two orphaned verdicts (p03 r1, p01 r3), snapshotted p05's uncommitted round-2 work (f0c49f6). p02 round 1 was not integrated: its lists predate p05's list framework, so the host would not start; merge rolled back. |
| 2026-10-03 09:15 | Wave 1 resumed as two workflows: 1a' = p02 r2, p03 r2, p00 (judge r3, then r4); 1b' = p05 r2, p04 r2, p01 r4. |
| 2026-10-03 ~13:50 | Account usage limit reached again (reset 14:00 UTC). No verdicts in this stretch: builders for p02, p03, p04, p05 and p01 committed most of their round-2/4 work; p04's round-2 integrator and p00's round-3 critic were stopped. WIP snapshots taken where needed. |
| 2026-10-03 14:10 | Wave 1 resumed again from the builders' worktrees. needs-human #5 added (Arabic font licence, SIL OFL-1.1). |
| 2026-10-03 14:33 | Owner approved SIL OFL-1.1 for fonts (needs-human #5). Routed to p04's next round: bundle an Arabic font and record it in the licence gate. |
