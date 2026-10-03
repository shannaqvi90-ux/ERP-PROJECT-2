# p04 — Gates for the shell (written before the shell)

Date: 2026-10-03. Piece: p04-shell. Status: accepted.

| Gate | Where | What fails |
|---|---|---|
| Logical directions only | `tests/Erp.Gates.Tests/Rules/ShellGateTests.cs` | `left`/`right` properties or values in any stylesheet, four-value margins/paddings/insets/borders whose left and right differ, asymmetric `border-radius`, physical inline styles in TSX. Ratchet: `rules.cssDeclarationsChecked`. |
| Formatting through the kernel | same | `toLocaleString`, `toFixed`, `new Intl.NumberFormat/DateTimeFormat` outside `kernel/format.ts` and `kernel/messageFormat.ts`. Ratchet: `rules.webFormattingFilesChecked`. |
| Menu group headings | same | a module's menu group without `shell.group.<group>` in English and Arabic. |
| Own preferences audited | `Rules/AuditGateTests.cs` | `PUT /me/preferences` leaving no field-level audit row, or recording an unchanged field. |
| JSX accessibility | `web/scripts/check-a11y.mjs` (`npm run check`, also in the image build) | positive `tabIndex`, `onClick` on a non-interactive element without a role, icon-only buttons without a name, unlabelled inputs, `<img>` without `alt`. |
| Live accessibility | `tests/e2e/specs/shell.spec.ts` + `a11y.ts` | any control without an accessible name or text under WCAG AA contrast on sign-in, home, every menu screen, palette, help and preferences, in English and Arabic; a focused element without a visible focus ring. |
| Keyboard reach | `shell.spec.ts`, `AppShell.test.tsx` | a screen not reachable by palette or Alt+M; a shortcut missing from the help sheet; a shortcut that fails on an Arabic layout; two shortcuts on one chord (throws). |
| Permissions (G2 on screens) | `shell.spec.ts`, `AppShell.test.tsx` | the palette or pane offering a screen the user's roles do not grant, or a record source called without its permission. |
| Fresh visit | `shell.spec.ts` | a console error or 4xx response on a fresh visit, with or without a stale cookie. |
| Reload safety | `shell.spec.ts`, `AppShell.test.tsx`, `preferences.test.ts` | the language reverting after a reload right after switching. |

G1 is unchanged by this piece: no new endpoint or table. The one new column
(`identity.users.numerals`) and the new body field (`numerals`) sit on an existing tenant table and
endpoint, which the G1 HTTP attack already enumerates from the OpenAPI document.
