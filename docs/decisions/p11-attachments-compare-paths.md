# p11 — Attach a file and find it again: ours against Odoo

Date: 2026-11-09. Piece: p11-attachments, round 1. Status: accepted.

Task `attach-file` (`gauntlet/compare/tasks/attach-file.mjs`): the contact is open; attach `trade-licence-TL-2026-73519.pdf`; the file
is listed on the contact and held by the back end. The Odoo driver is the harness's own (paperclip, then "Attach files", then the file
in the file dialog). Ours: `gauntlet/compare/drivers/ours/attach-file.mjs`.

**Our path.** The contact's form carries its Attachments section under the fields, with an "Add files" button always visible. One step:
pick the file at that button (the click that opens the file dialog and the choice of the file are one counted step, as in the Odoo
driver's last action). The file is sent as soon as it is chosen and listed when the server has stored it (progress is in the same
section). Steps: ours 1, Odoo 2 or 3 depending on how the harness counts the paperclip click that only opens the attachment box
and the "Attach files" button; keystrokes: both 0 (left out of the comparison by the owner's zero rule); seconds: ours is a single
request that streams to disk, Odoo's goes through its upload and a re-render.

**Paths tried for fewer steps.** (a) A keyboard shortcut that opens the file dialog: one key press instead of one pick, no fewer steps
(the file choice is still a step) and a shortcut to keep conflict-free in the palette, so not added. (b) Opening the dialog on arrival
at the contact: a dialog that opens without being asked is a defect. (c) Dropping a file: the harness has no counted drag-and-drop
action that is shorter than a pick. If a critic finds the counts tied, the paths above are the ones tried and none shortens the
chain below one counted step.

**Not changed:** the harness's measuring core (p01 is frozen); the task file gained our product's declared end state (the list
endpoint and the upload it writes) and the two fixtures (remove an earlier copy; a health-check contact), like the other
tasks of the wave.
