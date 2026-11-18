# p18: what changed on the screens, and what did not

Built through the shared kernel components, so every module has it:

- **Shell.** Top bar on the surface colour with the Bizora wordmark (ink on light, white on dark and
  high contrast, the Arabic wordmark in Arabic; `docs/design/brand/` used as it is, copied to
  `web/public/brand/`), the command field with its glow, density and theme switches, language, help,
  user. Sidebar on the `--side` ground. Status bar of keys. The palette (Ctrl K) already searched the
  real endpoints and ran the real actions of every module; it gained the theme and density commands.
- **Lists.** Saved-view tabs under the heading (`ViewTabs`): the standard view, the built-in views
  and the shared and personal ones, one Tab stop, arrow keys move and open, counts when a screen asks
  (`viewCounts`, one `take=1` request per built-in view; the contacts list asks, the audit log does
  not, because counting a 700,000-row log on every open is not worth it). The views menu stays for
  saving, updating and deleting. Group by, sort, columns, grouped rows with their subtotals, and the
  details pane were already in the kernel list; they take the tokens. **Not built:** the trend and
  credit-bar cells of the sample's contacts list: no module offers a 12-month series or a credit
  figure through the API today, and a column with invented numbers would be a lie. The kernel takes
  the cell when a module adds the data.
- **Forms.** Section tabs under the record header (`SectionTabs`): one per section, jumping to it,
  all sections stay on one page, so no field is hidden and no keystroke is added to any task (a
  step-count rule of the owner: comparison tasks may not get slower). The record header with Save,
  Discard and Close is sticky. Dates are a day-first text box with a calendar button
  (`kernel/forms/dateInput.tsx`): it takes dd/mm/yyyy or yyyy-mm-dd in either set of digits and holds
  yyyy-mm-dd. The native date box is gone because it draws mm/dd/yyyy on an American browser.
- **Dates everywhere** are dd/mm/yyyy (`kernel/format.ts`), Gregorian, in the screen's digits.
- **Clipped text.** The attachments' Delete button and the currency box of a money field keep their
  whole words (`attachments.css`, `forms.css`); `design.spec.ts` fails on any button, input, tab or
  badge whose text overflows its box, in English and Arabic, in the three themes.
- **Home.** A dashboard of what needs doing: modules add cards through the new `home` extension
  slot (`kernel/home.tsx`); a card counts rows of its own list endpoints (failed jobs, users who never
  signed in, contacts) and an `attention` figure above zero counts toward "N things need you today".
  A user sees only the cards their roles allow.

## Tie-accepted and step counts

No existing task needs one more key or click: tabs are pointer helpers outside the Tab order (the
form's) or one stop that replaces nothing (the list's, roving), and the date box takes the same text
as before. See the notes of the round for the comparison runs.
