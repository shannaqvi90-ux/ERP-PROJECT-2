# p11 — Attach a file and find it again: ours against Odoo

Date: 2026-11-09. Piece: p11-attachments, round 1. Status: accepted.

Task `attach-file` (`gauntlet/compare/tasks/attach-file.mjs`): the contact is open; attach `trade-licence-TL-2026-73519.pdf`; the file
is listed on the contact and held by the back end. The Odoo driver is the harness's own (paperclip, then "Attach files", then the file
in the file dialog). Ours: `gauntlet/compare/drivers/ours/attach-file.mjs`.

**Our path.** The contact's form carries its Attachments section under the fields, with an "Add files" button always visible. One step:
pick the file at that button (the click that opens the file dialog and the choice of the file are one counted step, as in the Odoo
driver's last action). The file is sent as soon as it is chosen and listed when the server has stored it (progress is in the same
section). Measured on a local stack against the stored Odoo baseline (`gauntlet/reference/odoo/tasks/attach-file.json`):
steps 2 (open the file dialog, choose the file) against Odoo's 3 (paperclip, "Attach files", choose the file); clicks 1 against 2;
keystrokes 0 against 0 (left out of the comparison by the owner's zero rule); human seconds 4.15 against 6.8; machine seconds
0.157 to 0.21 over five runs (median 0.178) against 0.209. The file is listed from the upload's own answer (no second read of the
list), so the wait after the choice is one request of about 10 ms plus the page's render.

**Paths tried for fewer steps** (the builder's tries, as the owner's tie rule asks; ours already wins steps, clicks and human seconds): (a) A keyboard shortcut that opens the file dialog: one key press instead of one pick, no fewer steps
(the file choice is still a step) and a shortcut to keep conflict-free in the palette, so not added. (b) Opening the dialog on arrival
at the contact: a dialog that opens without being asked is a defect. (c) Dropping a file: the harness has no counted drag-and-drop
action that is shorter than a pick. If a critic finds the counts tied, the paths above are the ones tried and none shortens the
chain below one counted step.

**Not changed:** the harness's measuring core (p01 is frozen); the task file gained our product's declared end state (the list
endpoint and the upload it writes) and the two fixtures (remove an earlier copy; a health-check contact), like the other
tasks of the wave.
