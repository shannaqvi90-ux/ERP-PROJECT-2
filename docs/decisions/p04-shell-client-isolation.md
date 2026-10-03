# p04 — Tenant isolation in the browser: one identity per document

Date: 2026-10-03. Piece: p04-shell, round 3. Status: accepted.

## Problem

The round-2 critic planted a module-level `Map` in the command palette that cached record answers by
query (plant P9). Nothing cleared it at sign-out, so when tenant B signed out and tenant A signed in
on the same tab, A's palette listed B's users. Every gate passed: the G1 machinery covered only the
server, and no test ever signed in as two tenants in one browser. The real product showed a milder
form: the sign-in screen was pre-filled with B's e-mail after B signed out. The user this hurts is
real: an outsourced bookkeeper serving several client companies (tenants) from one browser.

## Decision

1. **A document holds one identity for its whole life** (`web/src/kernel/session.tsx`). The identity
   is the tenant and the user (`identityOf`). Signing out, a session that ends (any API request
   answered 401 outside `/api/auth/`, which raises `erp:session-ended`; the session check then finds
   no session) and a session that turns out to be another identity (another tenant or user, for
   example after switching workspace) all end it: the browser forgets what it stored for the
   identity and the document is replaced with `location.replace("/")`. A fresh document has no
   JavaScript memory of the previous identity: no module state, cache, React state or closure can
   carry anything over, whatever a future module does. While it ends, the screen shows "Signing out…".
2. **What the browser stores is forgotten** (`web/src/kernel/deviceState.ts`): every localStorage key
   except the device's own settings (`erp.language`, `erp.numerals`, `erp.navOpen`), all of
   sessionStorage, every IndexedDB database and every Cache Storage cache. The remembered sign-in
   e-mail (`erp.lastEmail`) is forgotten when the person signs out and kept when the session simply
   ended (expiry, browser closed): the same person usually returns then, and the returning sign-in
   stays "password, Enter".
3. **Back cannot bring a signed-out screen back**: `replace` takes the signed-in page out of the
   history, and a page restored from the back/forward cache reloads (`pageshow` with `persisted`,
   `web/src/main.tsx`). API responses already carry `Cache-Control: no-store`.
4. **Module caches are identity-scoped**: a module that wants an in-memory cache makes it with
   `identityScoped()`, emptied when the identity ends (the document is replaced anyway).

## Gates (written before the fix, made to fail on the plants)

- **Inventory** (`tests/Erp.Gates.Tests/G1/G1ClientStateTests.cs`): every module-level binding in the
  web sources that is not a constant, a function, a React context, a glob of code, an
  identity-scoped map or a module's `routes`/`extensions` registration (a top-level `let`, a
  top-level `new Map()`, `[]`, `{}`, a class's static field, a property put on `window` or
  `globalThis`) must be reviewed in `tests/Gates/client-module-state.txt` with the reason it can
  never hold tenant data. The self-test plants P9 exactly as the critic did, in the product's own
  palette source. Ratchet: `g1.clientSourceFilesScanned`, `g1.clientModuleBindingsInspected`.
- **One tab, two tenants, memory kept** (`web/src/modules/shell/clientIsolation.test.tsx`): tenant B
  uses every menu screen, the palette with every record source, a record and the dialogs, and signs
  out; the App is mounted again in the same JavaScript memory (the harder case: the product would
  replace the document); tenant A takes the same journey. After every step the page, every input,
  the title, localStorage and sessionStorage are searched for B's markers. `web/scripts/plant-self-test.mjs`
  (run by `./erp verify`) applies five plants to a copy of the sources (P9; P9 inside an
  identity-scoped map whose reset is skipped; the e-mail kept at sign-out; localStorage and
  sessionStorage not cleared) and requires the gate to fail on an assertion for each, after a
  control run that passes.
- **The real browser** (`tests/e2e/specs/client-isolation.spec.ts`): the same journey against the
  running product with marker users created through the API in both tenants. After B signs out and
  after every step of A, the judges search the page, inputs, title, localStorage, sessionStorage,
  every IndexedDB database, Cache Storage and **a V8 heap snapshot** (after a full garbage
  collection) for B's markers: its token, tenant code and names in both languages, tenant id, user
  ids. The test never sends a marker into the page, so the heap holds one only if the product does.
  Every API response must carry `Cache-Control: no-store`. A self-test plants a marker in each of the
  eight carriers and requires each judge to find its own; a further test checks that Back after
  signing out shows only the sign-in screen. Against a build with P9 and without the document
  replacement, the gate reports B's token, tenant code and ids in A's heap, page and inputs.

## Why not only clear the caches

Clearing only what we know about is the round-2 failure mode: the next module adds a cache nobody
clears. Replacing the document removes every in-memory carrier at once, at the cost of one page
load at sign-out (the next person signs in anyway). The inventory and the memory-kept unit gate stay
as defence in depth, so a cache that would leak without the reload is still caught.

## For other pieces

- p02's workplace switcher changes company and branch within one tenant and user; that is not an
  identity change and does not reload. Anything that changes the tenant or user of the session must
  end in a session `refresh()` (or a new sign-in): the session provider then starts over.
- New module-level state in `web/src` needs an entry in `tests/Gates/client-module-state.txt`, or
  `identityScoped()` for caches.
