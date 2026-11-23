# p18: design tokens, themes and density

Owner's choice (needs-human #52, "G final"): direction G of docs/design/samples/g.

## Decisions

1. **One token file.** `web/src/design/tokens.css` is G's `tokens.css` (colour, type, space, radius,
   elevation, glow, motion, frame sizes) for dark (the root), light and high contrast, plus two
   things G did not have: names for the variables the screens used before (`--text`, `--muted`,
   `--border`, `--danger-bg`, `--warn`, `--surface-alt`, `--nav-bg`, `--row-height`, `--font`, `--mono`
   ...) defined as aliases of the new tokens, so every existing rule took the theme at once; and a
   `@media print` block that puts the paper colours back to light whatever theme the screen shows.
   Reason: a rewrite of 4,000 lines of module CSS would have cost more than it gives; aliases keep
   old rules working while new rules use the token names.
2. **No colour literal outside the tokens.** The hex and rgb colours that were in `styles.css`,
   `lists.css`, `forms.css` and the module stylesheets (73) now name tokens.
   `web/src/design/designGate.test.ts` fails on any new literal, on a `var(--x)` that nothing
   defines, on a colour token a theme lacks, on text below 4.5:1 (7:1 in high contrast) against any
   ground in any theme, on a native `<input type="date">` (outside `dateInput.tsx`), on a physical
   (left/right) CSS property, and on a font the tokens name but the product does not ship.
3. **Theme and density are device settings.** `erp.theme` (`system|light|dark|hc`) and
   `erp.density` (`compact|comfortable`) are two new device keys (`kernel/deviceState.ts`,
   `tests/Gates/device-keys.txt`, each with its value pattern), like the language. Until a theme is
   chosen, the key is absent and the screen follows `prefers-color-scheme` and `prefers-contrast`
   live (`kernel/theme.ts`); pressing the chosen theme again returns to the system. `main.tsx`
   applies them before the first render, so a reload never flashes the wrong look.
   *Why not the user's profile:* the server stores language and digits in the identity module's
   preferences endpoint; adding two fields needs a migration and changes to that module. A device
   setting meets "remembered, chosen by the person" on their own browser; syncing it between a
   person's devices is a later step for the identity module and is not built here. The key stays
   after sign-out by design (the device's look, not data: its value patterns are reviewed), so the
   next person on a shared PC sees the same look until they change it.
4. **Print stays on paper colours.** Printing from a dark screen produces black on white.

## Cost

Web only; no endpoint, table or job. Processor time added to the verify: the design gate (about
0.3 s), 12 new web unit tests and 1 Playwright spec with 14 tests (about 90 s of browser time).
