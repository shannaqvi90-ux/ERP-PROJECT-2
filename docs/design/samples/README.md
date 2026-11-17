# p18 design samples (stage 1)

Three directions, each with `tokens.css`, `style.css`, 8 pages (`contacts`, `contact-form`, `settings`, `sign-in`, each `.en.html` and `.ar.html`), `note.md` and `screenshots/`.
Open any page with `file://`; add `?theme=dark` or `?theme=hc`, and `?density=compact` or `?density=comfortable`.
`tools/build.mjs` regenerates the pages from `tools/lib.mjs` (content) and `tools/dir-*.mjs` (layout); `tools/shoot.mjs` takes the screenshots (needs playwright-core and Chromium); `tools/data.json` is a 36-row demo subset of the comparison contacts file (currencies other than AED, and three cities, are demo overrides).
Fonts in `fonts/` are all SIL Open Font License with their licence files. Brand files are used as they are from `../brand/`.
