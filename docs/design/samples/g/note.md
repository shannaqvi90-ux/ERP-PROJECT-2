# Direction G: "Command centre, best of all"

The owner's brief after round 2: "F is good, but it has no command palette. Otherwise I liked D's colours and design." G is F's structure and working touches, drawn in D's visual language, with D's command palette working on every screen. Dark is the default (light and high contrast use the same tokens), Compact rows are the default, and every screen comes in English and in full right-to-left Arabic.

## What comes from F

- **Layout.** F's sidebar (both wordmarks and the app icon, collapsible module groups with counts, recent records, the signed-in user at the foot), F's page header (title, record count, Import, Export, more, one primary action), and F's list with a details pane beside it.
- **Saved-view tabs with counts:** All contacts 100,000, My customers 412, Overdue 14, Credit stop 3, Free zone 2,318, Save view.
- **Rows grouped by status** (Credit stop, On hold, Active), each group header sticky under the column header, collapsible, with its count and the overdue and balance subtotals converted to AED, the base currency.
- **Group by / Sort / Columns** controls in the toolbar, with the search field (`/`), the active filter chip and Add filter.
- **Next best action** on the record: "3 invoices overdue · 18,420.00 AED", why it matters, Send statement (S) and Remind me tomorrow. It sits at the top of the details pane and as a banner on the contact form.
- **Order timeline:** Quoted, Ordered, Delivered, Invoiced (current), Paid (due 14/12), horizontal in the pane, vertical beside the form.
- **Dashboard content:** the greeting and "4 things need you today", the four action cards (overdue invoices, approvals, stock to reorder, credit limits breached) each with its count, total, top three items and one action; five key figures with 12-month bars; recent activity; the recurring-invoices empty state.
- **Contact form:** record header with key facts, tabs (General, Addresses 2, Accounting, People 2, Documents 3, History), sections (Identity, Accounting and credit, Contact and address), money fields with a currency chip and the AED line (AED is "Base currency"; the USD field shows "≈ 45,832.80 AED at 3.6725"; KWD, BHD and OMR take three decimals), dd/mm/yyyy dates with a picker and the licence-expiry warning, one inline error (Accounts email), the order timeline, credit with its 12-month trend, the record's audit facts, and the sticky save bar with change and error counts.
- **Settings and sign-in content:** users grid, saved-view style tabs, filter chips with counts, access changes this month, sign-in policy, roles list with the open role's permission matrix, the 50,000.00 AED approval note and members; sign-in as one centred card with both wordmarks, Caps Lock warning, passkey and company sign-on, and the three trust points under it.

## What comes from D

- **Palette and tokens.** `tokens.css` is D's file copied as it is; the only change is a marked "G additions" block in each theme for tokens F's components need and D lacks: `--side-w`, `--pane-w`, `--aside-w`, `--roles-w`, `--side` (sidebar ground), `--group-bg` (group header rows), `--nba-bg` and `--nba-line` (the next-best-action note). Dark: ground #08070D, surfaces #0F0E17 / #14131E / #1A1926, hairlines #22202F, accent #9B5CFF. Light: ground #F3F3F7, white surfaces, accent #6A00E8. High contrast: black, white rules, yellow focus, no glow. Status colours are D's quiet mint, amber, coral and blue; overdue amounts are amber, negatives coral in brackets.
- **Type.** Geist for English, Geist Mono for codes, references, TRNs, dates in lists and keys, Readex Pro for Arabic (all SIL OFL, bundled in `../fonts/` with their licences). Arabic is one step larger; dense one-line labels use 1.5 leading, running text 1.6 to 1.7.
- **Crisp high-information grid.** Edge-to-edge split view on hairlines instead of F's floating cards and shadows; sticky header with a strong rule; mono TRNs at a fixed 12 px so all 15 digits fit; credit-used micro-bars; D's tiles (Balance with a sparkline, Overdue with a red top edge, Credit limit with its bar) in the details pane; D's small capital section labels.
- **Restrained glow**, in four places only: the command field, the primary action, the active tab (saved views and form tabs) and focus.
- **Keys everywhere.** Every keyed button shows its keys (New contact N C, Statement S, New invoice N I, Invite user I, Save Ctrl S, Discard Esc, the dashboard actions R, A, O, C), and D's status bar at the foot of every screen shows sync state, company, base currency and the keys for that screen.
- **Command palette (Ctrl K)**, ported from D's markup and styles and made fully working (`palette.js`).

## The command palette

- Opens with **Ctrl K** (or Cmd K) on every screen, from the glowing "Jump to, search or run a command…" field in the top bar, and from the "Ctrl K, from anywhere" card in the sidebar. On the sign-in screen the field reads "Language, theme or help…". Add `?palette=open` to any page to see it open (on contacts it opens with the query "al wa", or "الوصل" in Arabic).
- **Finds records:** all 36 demo contacts (by name in either language, reference, TRN, city, email or phone), seven invoices and four orders (sales orders, a purchase order, a quotation). Up to six records show, mixing contacts, invoices and orders.
- **Runs actions on them:** the top contact match brings "Send statement to …" (with its overdue figure), "New invoice for …" and "Record payment from …". **Tab** on a highlighted record lists all its actions (open, statement, invoice, payment, email, call, copy TRN for a contact; open, email, payment, print in both languages, credit note for an invoice); Shift+Tab, Backspace on an empty field or Esc goes back.
- **Commands and Go to:** New contact, New invoice, Record payment, Send overdue reminders, Review approvals, Invite user, switch theme, switch density, switch language, print, sign out; Home, Contacts, Sales invoices, Receivables ageing, Purchase orders, Stock levels, VAT returns, Users and roles. Type `>` for commands only, `#` for documents only.
- **Keyboard:** Up and Down move (wrapping), Page Up and Page Down jump to the ends, Enter runs, Esc closes. The field is a combobox over a listbox with `aria-activedescendant`, so screen readers follow the highlight.
- **Arabic:** the palette is right to left with Arabic labels, groups, hints and messages; matching ignores short vowels and alef, ya and ta marbuta forms; amounts and references stay left to right inside Arabic lines.
- With no query it suggests what fits the screen: actions on the open record (contacts and form), what needs you today (home), Invite user (settings), recent records, Go to and common commands.
- Home, Contacts, Users and roles, sign out and Al Wasl Cables Co. (the one full record in the sample) open the real sample pages; other actions show a confirmation toast that says it is a sample and nothing was sent.

## Components

Sidebar (brand, groups with counts, Pinned with counts, Recent, palette tip, user); top bar (breadcrumb, command field, density switch, theme, language, notifications, help); status bar; page header with saved-view tabs; toolbar with search, filter chips and Group by / Sort / Columns; grouped grid with sticky AED subtotals and footer total; details pane (next best action, tiles, order timeline, details); dashboard action cards, key figures with bars, activity feed, empty state; record header with facts and tabs; three-column form sections, money field, date field, inline error, save bar; users grid, access feed, policy, roles and permission matrix; sign-in card; command palette and toast.

## Rules kept from A to F

Brand SVGs from `../../brand/` used as they are (both wordmarks, white versions in dark and high contrast, the app icon); the density switch always visible; one primary action per screen (New contact, Send reminders, Save, Invite user, Sign in); the currency after every amount, negatives red in brackets, three decimals for KWD, BHD and OMR; 15-digit TRNs and Gulf data; truncated text carries a tooltip; icons are our own inline SVG paths.

## Why better than D and F

Than F: the same smart list, record and form, with D's denser, sharper grid (more rows and columns in view), a real dark theme, keys shown on every action, and a command palette that actually finds and acts. Than D: F's saved views, grouped subtotals in base currency, next-best-action hint, order timeline and a sidebar that names every module instead of an icon rail, so the system is easier to learn without losing D's keyboard speed.

## Limits

- The palette searches the demo data embedded in each page (36 contacts, 7 invoices, 4 orders); in the product it would query the server. Actions other than navigation only show a sample toast.
- On sign-in the palette offers language, theme, density, password reset, passkey, single sign-on, status and help only, since records need a signed-in user.
- The contacts screenshot keeps the grid at 30 rows and the pane at the top of its scroll; the form scrolls under the save bar as in D and F.
