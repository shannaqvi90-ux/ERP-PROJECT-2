# Gate backlog

Gate gaps that no longer block a round (owner decision of 2026-10-23, needs-human #27, recorded
in `gauntlet/goal.md`). A gate gap is a fault a critic planted in its own clone that the gate
suites missed while the real product showed no such fault. Each row is work for the piece that
owns the gate. Gates only get stricter: rows are closed by a stricter gate, never dropped.

Rows marked **uncovered surface** are surfaces a piece added that the isolation or permission
gates do not reach at all. Under the decision these would block a new round; they are listed
first and are worked first in the owning piece's next round or backlog round.

Recorders append one row per gate gap a verdict reports. The lead marks a row Closed with the
commit and verdict that closed it.

| # | Added | Owner | From verdict | Kind | Gap | Status |
|---|---|---|---|---|---|---|
| 1 | 2026-10-23 | p03-identity | p03-identity r13 (047f1920) | **uncovered surface** | The screen permission gate does not cover the 'Let a device sign in' form on the user's Sign-in history tab: removing `canUnblock &&` from canApprove offers the form and its palette command (identity.users.approveDevice) to anyone who can read the sign-in history, and every web unit, sweep and e2e test passes. | Open |
| 2 | 2026-10-23 | p06-form-report | p06-form-report r9 (fe7e8d79) | **uncovered surface** | No isolation or permission gate prints a document longer than the screen (ReportLimits.ScreenRows, 2,000), so the whole-selection PDF/CSV/XLSX path is untested: a tenant-blind PDF spool and a missing permission check in big files pass every gate. | Open |
| 3 | 2026-10-23 | p00-foundation | p00-foundation r14 (810929c2) | planted variant | G2 checks only the client address: a kernel permission rule that trusts the request's Host header (plant P13, Host 'app') passes all G2, rule, self, G1 and module tests. | Open |
| 4 | 2026-10-23 | p04-shell | p04-shell r12 (9140b150) | planted variant | The browser tenant-isolation journey never uses the shell's own language switch (Alt+L, the top-bar language button, the digits switch) while tenant B's data is on screen, so a device-kept key carrying a record to the next person's tab (plant CL3b on 'erp.language') passes. p04 r13, built before this decision, works on it and is being judged in serial-10c. | Open |
| 5 | 2026-10-23 | p01-odoo-rig | p01-odoo-rig r14 (b1dd24aa) | measurement (p01 frozen) | Find tasks can still be done off the clock: set-up's reads with side effects, names drawn outside innerText (CSS content, images, shadow roots), and server-timed polls pass every round-14 rule. Fixed only if one of these changes a real comparison result. | Open |
