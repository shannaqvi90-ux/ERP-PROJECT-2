# p04 — Arabic-capable fonts without a licence exception

Date: 2026-10-03. Piece: p04-shell. Status: accepted for now; a bundled font waits for the owner.

## Decision

- The shell uses the operating system's UI fonts, which cover Arabic on every desktop the product
  targets: Segoe UI and Tahoma (Windows), SF Arabic / Geeza Pro (Apple), Noto Sans Arabic and
  DejaVu Sans (Linux). Arabic text (`:lang(ar)`) gets an Arabic-first stack.
- No web font is bundled. Every maintained Arabic UI font found on npm (Noto Sans Arabic, Noto
  Kufi/Naskh Arabic, IBM Plex Sans Arabic, Cairo, Tajawal, Vazirmatn) is under the SIL Open Font
  Licence 1.1, which is not on the CLAUDE.md rule 6 allowlist (MIT, Apache-2.0, BSD, PostgreSQL)
  and carries copyleft-style terms for the font itself. Adding one needs the owner's approval:
  listed for `gauntlet/needs-human.md` by the lead. If approved, `@fontsource/noto-sans-arabic`
  (OFL-1.1) is a one-line import in `main.tsx` plus the family name at the head of `--font-ar`.

## Why

- Rule 6 does not allow an OFL dependency without a human decision; the system fonts give correct
  Arabic shaping today on all supported desktops.
