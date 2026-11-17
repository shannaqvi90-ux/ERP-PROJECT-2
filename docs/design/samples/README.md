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

## Using the samples

Open any page with `file://`; add `?theme=dark`, `?theme=light` or `?theme=hc`, and `?density=compact` or `?density=comfortable`. D opens in dark by default.

`tools/build.mjs` regenerates the pages: A to C from `tools/lib.mjs` (content) and `tools/dir-a|b|c.mjs` (layout); D to F from `tools/lib2.mjs` (all of lib.mjs plus the dashboard, derived contact figures, micro charts, the command palette, saved views, grouped rows, the timeline and next-best-action content) and `tools/dir-d|e|f.mjs`. `tools/shoot.mjs` takes the screenshots (needs playwright-core and Chromium; `ONLY=contacts,dashboard` limits the pages). `tools/data.json` is a 36-row demo subset of the comparison contacts file (currencies other than AED, and three cities, are demo overrides); KWD, BHD and OMR amounts show three decimals in D to F.

Fonts in `fonts/` are all SIL Open Font License, with their licence files: IBM Plex Sans and IBM Plex Sans Arabic, Inter, Noto Sans Arabic, Manrope, Fraunces, Noto Kufi Arabic, Noto Naskh Arabic (round 1), and Geist, Geist Mono, Readex Pro, Source Serif 4, Schibsted Grotesk (round 2). Icons are drawn for these samples as inline SVG paths. Brand files are used as they are from `../brand/`.
