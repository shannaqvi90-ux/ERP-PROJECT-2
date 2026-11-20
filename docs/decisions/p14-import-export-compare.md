# p14 — Comparison with Odoo: import 5,000 rows

Date: 2026-11-18. Piece: p14-import-export, round 1. Status: accepted.

Odoo's recorded path (gauntlet/reference/odoo/tasks/import-5000.json): 7 steps (6 clicks and one file pick), 0 keystrokes,
7.3 machine seconds of which 6.6 are the system working, 17.4 modelled human seconds, 24.0 with the wait.

Ours (driver `gauntlet/compare/drivers/ours/import-5000.mjs`): click Contacts, click Import (or Alt+I), pick the file (its headers
Name, Email, Phone, Street, City, Country match fields by themselves), click Import 5,000 rows: 4 steps, 0 keystrokes, and a file of up
to 5,000 rows runs inside the request (no job pick-up wait). Measured in the test suite on a loaded machine: 5,000 rows are written in
2.5 s of bulk insert plus 0.3 s of checking. The critic measures the comparison on the demo stack.

Why the steps cannot be fewer: choosing the file is one step in both products; the import needs one confirmation (the person may change
a column first); the dialog opens from the list's toolbar, one click after the list. A path through the command palette (Ctrl+K,
"import") costs more keystrokes than the click.
