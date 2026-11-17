# p18 design samples (stage 1)

## Round 1: A, B and C

Three directions, each with `tokens.css`, `style.css`, 8 pages (`contacts`, `contact-form`, `settings`, `sign-in`, each `.en.html` and `.ar.html`), `note.md` and `screenshots/`.

- **A "Fluent, faster"**: closest to Dynamics 365, dense, ink app bar, tree navigation.
- **B "Quiet and light"**: modern software-as-a-service feel, few lines, airy.
- **C "Majlis"**: calm and premium, sand and gold, the best Arabic typography of round 1.

## Round 2: D, E and F (the owner asked for "something even better")

Each adds a fifth screen, the **dashboard** that says what needs doing today, so each has 10 pages (`dashboard`, `contacts`, `contact-form`, `settings`, `sign-in`, in English and Arabic), 20 screenshots in light and dark, plus `contacts.<en|ar>.hc.png` in high contrast.

- **D "Command centre"**: dark-first (light too), the keyboard is the hero: a command palette on Ctrl K (`screenshots/command-palette.*.png`, or add `?palette=open` to the contacts page), split views, a high-information grid with credit-used bars and trend sparklines, glow used sparingly.
- **E "Financial clarity"**: editorial and typographic, superb number typography, inline sparklines and micro-bars in the grid, restrained colour.
- **F "Best of all"**: a synthesis of A's density, B's lightness and C's Arabic and calm, with saved-view tabs, grouped rows with sticky subtotals, an inline status timeline and a next-best-action hint.

## Round 3: G (the owner: "F is good, but it has no command palette. Otherwise I liked D's colours and design.")

- **G "Command centre, best of all"**: F's structure and working touches (sidebar with recent records, page header, list with details pane, saved-view tabs with counts, rows grouped by status with sticky AED subtotals, Group by / Sort / Columns, the next-best-action hint, the order timeline, F's dashboard, form, settings and sign-in content) in D's colours and design (D's tokens copied with a marked "G additions" block, Geist, Geist Mono and Readex Pro, the crisp grid, glow only on the command field, primary action, active tab and focus, keys on buttons, the status bar). D's command palette is ported and made to work on every screen: Ctrl K or the "Jump to, search or run a command" field, it finds contacts, invoices and orders, runs actions on them (Tab lists a record's actions), jumps anywhere, and works in Arabic, right to left. Same five screens as D to F, 20 screenshots in dark and light, `contacts.<en|ar>.hc.png`, and `command-palette.<en|ar>.<dark|light>.png`. See `g/note.md` for exactly what comes from F and what from D.

## Using the samples

Open any page with `file://`; add `?theme=dark`, `?theme=light` or `?theme=hc`, and `?density=compact` or `?density=comfortable`. D and G open in dark by default. In D and G, press Ctrl K or add `?palette=open` to see the command palette.

`tools/build.mjs` regenerates the pages: A to C from `tools/lib.mjs` (content) and `tools/dir-a|b|c.mjs` (layout); D to G from `tools/lib2.mjs` (all of lib.mjs plus the dashboard, derived contact figures, micro charts, the command palette, saved views, grouped rows, the timeline and next-best-action content) and `tools/dir-d|e|f|g.mjs`; G's palette data is embedded in each page and run by `g/palette.js`. `tools/shoot.mjs` takes the screenshots (needs playwright-core and Chromium; `ONLY=contacts,dashboard` limits the pages). `tools/data.json` is a 36-row demo subset of the comparison contacts file (currencies other than AED, and three cities, are demo overrides); KWD, BHD and OMR amounts show three decimals in D to G.

Fonts in `fonts/` are all SIL Open Font License, with their licence files: IBM Plex Sans and IBM Plex Sans Arabic, Inter, Noto Sans Arabic, Manrope, Fraunces, Noto Kufi Arabic, Noto Naskh Arabic (round 1), and Geist, Geist Mono, Readex Pro, Source Serif 4, Schibsted Grotesk (round 2). Icons are drawn for these samples as inline SVG paths. Brand files are used as they are from `../brand/`.
