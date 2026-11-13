# p11 — Finding a file again: the record shows that it has files, and one click opens one

Date: 2026-11-12. Piece: p11-attachments, round 2. Status: accepted. Answers round 1's biggest gap (finding a file again tied Odoo: scroll,
then Preview, 2 steps and 5.3 s modelled in both; by keyboard 61 Tab presses to Add files and no shortcut).

## What a record shows now

- **Toolbar.** A paperclip button with the number of files sits beside Print and History in the record's toolbar. It goes to the files (scrolls
  the section into view and puts the cursor on the first file's name, on Add files when there is none). Its tooltip and `aria-keyshortcuts` name
  the key, **Alt+A**; the key is registered like the form's other keys (`useCommand`), so the shortcut help sheet and the command palette list
  it ("Go to the files" / "الانتقال إلى الملفات"). Alt+A was free (the form uses Alt+R, Alt+Y, Alt+Z, Alt+PageUp/PageDown); Alt+F was left
  alone because browsers open their menu with it.
- **Names under the heading.** The newest three files are named on the line under the record's title; one click opens a file in the viewer
  (picture, PDF, text), any other kind is saved. "N more" goes to the section. This is the path that wins the comparison: the file is on the
  screen when the record opens.
- **Same extension point as History.** Both are the attachments module's own record panel (`web/src/modules/attachments/extensions.ts`): the
  kernel's `RecordPanelItem` gained two optional fields (`toolbar`, a button of the panel's own; `always`, a panel drawn without being opened),
  nothing else about the kernel changed. A kind of record takes files by being listed in `attachmentKinds` (next to the server's
  `AttachmentRecord` registration); the contacts form no longer renders the section itself.
- **The count and the names cost no request.** The section is the one that reads the files (as before); it publishes what it read to a small
  store the button and the names line subscribe to.

## Global search (Ctrl+K)

The palette has a public registration (`extensions.palette`). The attachments module registers one source per kind of record that takes files:
`GET /api/attachments/{kind}/files?search=&take=` (the fifth attachment endpoint, under the kind's read permission; the match is a
case-insensitive part of the name, wildcard characters are text, at most 20, each file's record is checked to be visible to the caller). Choosing
a result opens the record and shows the file in the viewer once the record's files have loaded. The endpoint adds one row to the reviewed
permission map and one to the reviewed client-input list (below); it answers `matches`, not `items`, so it is not taken for a registered list.

## Measured: open-attached-file (gauntlet/compare, the critic's task of round 1, now in tasks/ and drivers/)

Start: the contact is open with the file attached (and a second one). Goal: the file is open on screen in a viewer. Odoo: paperclip, then the
file's card (2 steps). Ours: the file's name under the heading (1 step). Median of three runs each, the shared Odoo rig and a local stack of this branch (`--product both --repeat 3`):
steps 1 against 2; human seconds 2.65 against 5.3; machine seconds 0.073 against 0.252; keystrokes 0 against 0 (left out by the owner's zero rule).
Harness verdict: win. `attach-file`, measured beside it: steps 2 against 3, human seconds 4.15 against 6.8, verdict win (unchanged by this round).
Processor time this round adds to the full verify: one endpoint (the file search) in the gates, three gate tests, no new data volume; the builder
did not run a full verify (owner decision #41).

**The task's file is a picture, not a PDF.** The harness marks a run invalid when the page changes without a counted step causing it. A PDF
shown in the browser's own PDF viewer lays itself out in its own frame after it loads (a `LayoutInvalidationTracking` task of no traceable origin
in the frame's renderer, 20 to 30 ms after the document's load): five of six runs of the PDF variant were invalid whatever the page did
(a settled-viewer delay, the viewer's toolbar switched off with `#toolbar=0`, a loading flag), and an invalid health check fails `./erp verify`
(`build/verify-inside.sh`). The picture variant verified six of six. The path is the same one click for a PDF; the harness (p01, frozen) cannot
measure the browser's PDF frame. Odoo shows its PDFs with its own script viewer, which the harness can trace.

**Paths tried for fewer than one step:** none is shorter: opening the file is one counted action, and the file is already named on the screen.
By keyboard: Alt+A, Enter (two key presses, no Tab walk) against Odoo's pointer-only path.

## Viewer

Text files (`txt`, `csv`, up to 512 KB) are shown as text in the viewer (fetched from the download, set into a `pre` element, never as a page), in
the reading direction of their own text (`dir="auto"`). The viewer says when it holds the file (`data-loaded`, `aria-busy`), a PDF's frame after
a short settle, so a reader and the instrument both know when to read.

## The attachments table fits its section

The section's table no longer cuts the buttons ("De"): the name column takes what is left and ends in an ellipsis, the actions keep their words,
and on a section narrower than 640 px (the details pane beside a list) each file is two lines, its name above and, below, who added it with the
buttons; size and date are left to wider sections (container query, logical properties only, so Arabic mirrors). Checked at 1440 x 900 in the
end-to-end suite (every button whole and inside the section).

## Round 2 fix: role form 3 px wider than its panel (audit.spec.ts:54)

The builder's e2e run failed audit.spec.ts:54 (and, by a value left behind, navigation.spec.ts:93 and print-paper.spec.ts:297 ar). The
cause was not this round: in the Roles form at 1280 px, long "Other" permission labels of the Background jobs block ("Cancel
background jobs", "Download what background jobs ...", "Start and re-run background jobs") sat 3 px past the form's edge (matrix
cells are fixed-width). Fix: identity.css lets an "Other" label take its cell's width and wrap (max-inline-size 100%, no end margin,
overflow-wrap anywhere). Re-run on a fresh stack (volume 2000): whole Playwright suite 156 passed in 10.0 min; web identity tests 114 passed.
No full verify run (owner decision #41); no processor time added by this fix (CSS only).
