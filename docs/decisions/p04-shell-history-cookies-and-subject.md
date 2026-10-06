# p04 — What outlives a document in a tab, and actions on "me"

Date: 2026-10-05. Piece: p04-shell, round 4. Status: accepted. Builds on
`p04-shell-client-isolation.md` (one identity per document).

## Problem (critic p04 round 3)

Round 3 made every identity end in a fresh document and cleared storage, IndexedDB, Cache Storage
and memory at sign-out. The critic found three carriers that outlive a document and that no gate
looked at:

1. **The tab's history.** Lists keep their state in the address (`?q=` search text, `?open=` record
   id, filters). `location.replace("/")` at sign-out replaces only the current entry. When tenant A
   signed in in the same tab and pressed Back, A's Users screen opened at
   `/identity/users?q=Khalifa+Steel+Supplies`: B's supplier name in A's search box, and B's record
   e-mail and id from a palette jump.
2. **Cookies a script can read** (plant C2: the last record opened from the palette in a cookie).
3. **`window.name`** (plant C1: palette answers kept there).

And one hole in G2 (plant N1): `PUT /me/preferences` with an optional, documented `userId` changed
another user's preferences. The caller held the endpoint's permission, so every permission gate
passed; the fault is in which record the handler picks.

## Decisions

### History entries carry the identity that wrote them (`web/src/kernel/historyGuard.ts`)

- The tab gets an **identity epoch**: a random 96-bit value in sessionStorage (`erp.historyEpoch`),
  never an id or a name. `forgetIdentity()` clears sessionStorage whenever an identity ends, so the
  epoch ends with it.
- `history.pushState` / `history.replaceState` are wrapped once, in `main.tsx` before the first
  render, so **every entry the app writes** (router, lists, modules, future modules) carries the
  epoch in `history.state`, merged into the caller's own state.
- **On load**, the entry the document opened on is trusted only if it carries the current epoch, or
  if the document was reached by a fresh navigation (a typed address, a bookmark, a link: Navigation
  Timing type `navigate` or `prerender`), which is what the person in front of the tab asks for now.
  Back, Forward and reload (`back_forward`, `reload`, or an unknown type) to an entry without the
  current epoch replace it with `/` before any screen reads the address.
- **Inside a document**, a `popstate` listener registered before any screen's checks the entry the
  same way, so the router and screens never see an untrusted address.

Alternatives considered:

- *Keep tenant data out of the address altogether* (search, filters and record ids only in
  `history.state`). Stronger: nothing reaches the browser's global history (Ctrl+H) or the address
  bar's suggestions either. But it removes shareable addresses of a filtered list or a record, which
  keyboard-first users and support staff rely on, and it rewrites the list framework (p05) that is
  being reworked in parallel. Not taken now; see "Known limits".
- *Rewrite earlier entries at sign-out.* Impossible: a page cannot edit entries other than the current
  one.

### Cookies and `window.name` are forgotten (`web/src/kernel/deviceState.ts`, sign-out endpoint)

- `forgetIdentity()` expires every cookie the document can read, for every path prefix of the
  identity's screens and every domain the cookie could have been set on, and resets `window.name`.
- A browser shows a document only the cookies of the address the document was **loaded** at (Chrome
  ignores later `pushState` changes), so a cookie scoped to another path is invisible to the
  signing-out document. The sign-out answer therefore carries **`Clear-Site-Data: "cookies"`**: the
  browser drops every cookie of the site. Only `"cookies"`: `"storage"` would also drop the device's
  own settings (language, digits), which the shell keeps on purpose, and the shell clears the rest of
  storage itself. Caveat: browsers apply it to the registrable domain (all ports on `localhost`, all
  subdomains in production), so serve the ERP on a domain of its own.
- The product writes no cookie but the HttpOnly session cookie. The G1 client-state gate makes every
  use of a carrier that outlives the document a reviewed decision (`tests/Gates/client-carriers.txt`,
  counted per file), so a screen that starts writing a cookie or `window.name` fails before any
  browser runs (the critic's C1 and C2 are its self-test).

### G2 judges which record an action on the caller touches (`tests/Erp.Gates.Tests/G2/SubjectInjection.cs`)

Every write endpoint without a route parameter (it acts on the caller, the caller's tenant, or
creates; sign-in and sign-out included) is called by a caller holding exactly its permission (plus
the reads of the same collection), naming a victim (another user of the tenant, signed in, same role)
in the body (the usual subject names and every id the request schema declares), the query string and
the headers, one carrier per request. Every row of every tenant table that mentions the victim, and
the victim's own view of their session, must not change. The same request without the victim must
succeed, so a refused body never passes for a check. Planted bugs 43 and 44 in the gates' leaky
module (body and header) are its self-test; the critic's N1 diff fails it three ways (the victim's
user row, audit row and session).

## Known limits

- **Browser-wide history.** The browser's own history list (Ctrl+H) and the address bar's suggestions
  still hold the addresses B visited, with B's search text, until the browser's data is cleared. The
  app cannot reach them. On a device shared between companies, use a separate browser profile (or a
  guest window) per person; moving list state out of the address (above) would remove this too.
- A page restored from the back/forward cache is reloaded at `pageshow` (round 3); the reload is a
  `reload` navigation, so the guard checks its entry like any other.
