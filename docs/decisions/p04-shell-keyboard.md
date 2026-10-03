# p04 — Keyboard first: shortcuts by key position, one command palette

Date: 2026-10-03. Piece: p04-shell. Status: accepted.

## Decision

- **One registry, one listener** (`web/src/kernel/shortcuts.tsx`). A shortcut is
  `{ id, chord, labelKey, groupKey, run }`; chords are modifiers plus a `KeyboardEvent.code`
  (`Mod+KeyK`, `Alt+KeyL`, `Shift+Slash`). Matching by `code` (the key's position) makes every
  shortcut work unchanged on an Arabic keyboard layout, where K types "ن". `Mod` is Ctrl, ⌘ on
  Apple devices. AltGr (Ctrl+Alt) never matches an Alt shortcut, so typing characters is never
  captured. Shortcuts without Ctrl/Alt/⌘ never fire inside a text field; while a modal dialog is
  open only shortcuts marked `inDialogs` fire. Registering a chord that is already in use throws.
- **Shell shortcuts**: Ctrl+K command palette, Alt+L language, ? and Ctrl+/ shortcut help, Alt+P
  preferences, Alt+M navigation pane, Alt+H home, Alt+B show/hide the pane. Esc closes any dialog
  and gives the focus back. The help sheet lists every active shortcut (modules' included) with
  its translated label; key captions are always left to right.
- **Command palette** (Ctrl+K, or the search field in the top bar): screens, actions and records in
  one list. Screens and actions match at once against their text in *both* languages, after
  folding (case, Latin accents, Arabic short vowels and tatweel, أ/إ/آ→ا, ى→ي, ة→ه, Arabic-Indic
  digits). Record sources from modules are asked as the user types (debounced 120 ms, the older
  request aborted). Up/Down/PageUp/PageDown move, Enter opens, recent screens come first on an
  empty query. ARIA combobox + listbox with `aria-activedescendant`.
- Reaching a named screen by keyboard is Ctrl+K, two or three letters, Enter: 3 steps and about 6
  keystrokes from anywhere.

## Why

- `event.key` shortcuts break for every Arabic-layout user; `code` does not.
- Alt+letter chords avoid the browsers' own Ctrl shortcuts; Alt+digits switch tabs in Chrome on
  Linux, so digits are not used.

## Rejected

- A shortcut library (tinykeys, hotkeys-js): small, MIT, but they match on `key` by default and
  the registry with help-sheet integration is 200 lines.
