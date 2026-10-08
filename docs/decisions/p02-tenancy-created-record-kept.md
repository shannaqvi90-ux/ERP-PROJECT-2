# p02 — A record the form has just created is kept, not read again

Date: 2026-10-08. Piece: p02-tenancy (change in the shared record form, `web/src/kernel/forms/useRecordForm.ts`, owned by p06). Status: accepted.

## Context

The p01 round 6 critic's clean-clone verify failed once on the create-company-branch health
driver: after Ctrl+S on a new company and the branch name typed into the focused branch line,
Enter never produced the branch within 120 s. The same driver passed three times out of three on
a fresh demo, and the owner's setup session had seen the same miss once in five replays on
2026-10-05.

Cause: saving a new record points the screen at its id (`useRecordPanel.saved`), which changes
the key of `useRecordForm`. The key change started a read of the record just saved. While that
read ran, `RecordForm` drew "Loading" in place of the whole form, so the company's branch section
was removed and, when the read answered, drawn again with a fresh line holding only the company's
name, and the focus put back on it. Keys typed in between were lost. Enter then added a branch
named after the company, and the table never showed the typed name. On a quiet machine the read
answers before the first key; on a loaded one it does not. A person typing straight after Ctrl+S
lost keys the same way. Every screen that creates records through the shared form (branches,
users, roles) made the same needless read.

## Decision

- When a save creates the record (`record === null` before the save), the form marks it. If the
  next render changes only the key (not a `reload()`), the form keeps the saved answer as its
  record and does not read it again. The mark lasts for that one render only, so a later change
  of key (another record) or `reload()` (a conflict, a manual reload) still reads.
- The save's answer is the server's full record (POST returns what GET would), so nothing is
  lost by not reading it again.

## Alternatives not taken

- Make the health driver wait longer or check the branch line again before typing: that hides a
  product fault from the instrument; the driver was right.
- Fix only the company form (keep the key fixed after a create): the same fault sits in every
  create flow on the shared form.

## Tests

- `CompaniesPage.test.tsx`: with the company's read never answering, the branch line after the
  save is the same element, still focused, keeps what was typed, and Enter posts the typed name.
  Fails without the change ("Loading" is on screen).
- `forms.test.tsx`: a created record is kept with no GET and no Loading; `reload()` and another
  id still read. Fails without the change.
