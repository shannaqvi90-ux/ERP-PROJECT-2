# p00 — Front end: React 19 + Vite without plugins, per-module strings, tiny router

Date: 2026-10-02. Piece: p00-foundation. Status: accepted.

## Decision

- React 19, TypeScript (strict, `noUncheckedIndexedAccess`), Vite. JSX is compiled by esbuild's
  automatic runtime; **no `@vitejs/plugin-react`**, because it pulls Babel and `caniuse-lite`
  (CC-BY-4.0) into the dependency tree, which rule 6 does not allow without approval.
- Strings: each module keeps `src/modules/<module>/i18n/en.json` and `ar.json`; keys start with the
  module name. `scripts/check-strings.mjs` (run by `npm run check` and in the image build) fails on
  hard-coded JSX text or user-facing attributes, unknown keys, missing twins and empty values.
- Language: the user's saved language wins after sign-in, else the device's last choice, else the
  browser's. Switching sets `<html lang dir>` so the whole layout mirrors with CSS logical
  properties. API calls send `Accept-Language` so server messages match the screen.
- Routing: a 50-line history router (`kernel/router.tsx`) over module `routes.tsx` files; screens
  carry the permission they need and show "no access" instead of rendering when it is missing.
- Styles: one hand-written stylesheet with design tokens, dense (13px base, 28px controls),
  logical properties only (`margin-inline-start`, never `left`).
- Tests: vitest with happy-dom (unit), Playwright against the real stack (end-to-end).

## Why

- Every dependency is a licence and supply-chain liability; the shell needs very little.
- p04 (app shell) and p05 (list framework) will grow these kernels; keeping them small now keeps
  that work unblocked.

## Rejected

- react-router / TanStack Router: fine licences, but not needed for a dozen flat routes yet.
- i18next: MIT, but the required features are ~60 lines here and the string gate needs control.
