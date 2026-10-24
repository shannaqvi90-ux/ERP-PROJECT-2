# p05 — the list component keeps no row on the device: three gates of its own (round 15)

Date: 2026-10-24. Piece: p05-list-search (round 15, wave 2). Status: accepted.

## Context

Critic p05 round 14 (verdict BLOCKED, commit 9e3380e0) planted L14 in the list component's row
loader (`web/src/kernel/lists/useListRows.ts`): each query's first page written to the origin
private file system (`navigator.storage.getDirectory`), keyed by endpoint and query only, and shown
at once while the fresh page loads. Every client gate passed; live, the next tenant's admin on the
same browser opened Users on the first tenant's rows. The product itself does not use that store.

The shared client-isolation machinery (`G1ClientStateTests`, `kernel/deviceState` forgetIdentity,
e2e `client-isolation.spec.ts`) belongs to p04, and the missing origin-private-file-system carrier
there is gate backlog row 7, worked in a p04 backlog round. Those files are not touched here. What
p05 owns is the list component every 100,000-row list uses, so this round gives that component
gates of its own that do not depend on a list of known carriers.

## Decision

Three gates, each with planted self-tests, all inside p05's own files:

1. **Static: what the list kernel reaches in the browser** (`web/src/kernel/lists/browserReach.test.ts`).
   The list kernel's sources are read with the TypeScript checker (already a dev dependency,
   Apache-2.0), and every DOM-library member they reach is listed by the interface that declares it
   (`StorageManager.getDirectory`, `Storage.setItem`, `History.replaceState` ...). Resolving symbols
   means an alias (`const s = navigator.storage`), a quoted member (`window["localStorage"]`) and a
   new file of the kernel are seen like a direct call. Default deny:
   - members that die with the document (layout, focus, events, timers, query strings) are
     reviewed by member;
   - members that outlive the document or hand data on (storage, the origin private file system,
     IndexedDB, caches, cookies, `window.name`, history, the clipboard, workers, channels,
     `fetch`, beacons, file pickers, other windows) are reviewed per file with their exact count, so
     one more use is a new decision;
   - ways around the checker are refused outright: `any`, casting a browser object to `unknown`,
     computed members of browser objects, `globalThis` as a root, reflection, dynamic `import()`,
     `@ts-ignore`;
   - every module the kernel imports from outside its folder is reviewed.
   Stale reviews fail. 21 planted variants (L14 itself, its alias, IndexedDB, Cache Storage,
   cookies, `window.name`, sessionStorage, a second localStorage write, history state, a service
   worker, a broadcast channel, reflection, `any`, `unknown`, computed members, a new kernel file, a
   new import ...) are each refused. The applied L14 patch fails the gate.

2. **Behaviour: a list driven on a recording, poisoned device** (`web/src/kernel/lists/deviceStores.test.tsx`).
   Every store a page could keep data in is replaced by a recorder that also answers every read,
   for every key, with another tenant's first page. The real list is driven through first page,
   search as you type, sort both ways, a header filter, group and drill, End (offset paging),
   PageDown (keyset paging), selection and Select all, open a record, widen it, and save a view.
   It fails when any whole-value store is even opened (file system, IndexedDB, caches, channels,
   workers, service workers, locks, other windows), when anything put in any store carries a row's
   value or id (the address may carry the open record's id, its own address), when a request leaves
   `/api`, or when a poisoned row or count is on screen at any step, including while the server's
   answer is still on its way (60 ms late). The clipboard must hold nothing until Copy and the rows
   after it, which shows the recorder is not blind. Self-tests: L14 as a wrapped row loader (caught
   on the device and on screen) and a loader that keeps rows in sessionStorage, a cookie and
   `window.name`. The applied L14 patch fails both product tests.

3. **The real browser** (`tests/e2e/specs/lists.spec.ts`, 'keeps no row of a list on the device').
   After a person searches, sorts, pages to the end, selects and opens a record, the page reads
   everything the origin keeps: every file of the origin private file system (recursively),
   `indexedDB.databases()`, `caches.keys()`, all of localStorage and sessionStorage, the cookies a
   script sees, `window.name`, `history.state` and `navigator.storage.estimate()`. No file,
   database or cache may exist, no row value seen in the grid may appear anywhere, and the stored
   bytes may not exceed what the sign-in screen had, both signed in and after Sign out. Against a
   stack built with L14 applied it fails at once ('signed in: files in the origin private file
   system', the file holding alnoor rows).

The behaviour journey has its own 20 s budget, like the shell's G1 journey (it is twenty steps
long; each wait returns as soon as the screen gets there). That is the test's length, not an
allowance for load.

## What is left to p04 (not done here)

- The origin private file system as a carrier in `G1ClientStateTests` and the e2e
  client-isolation journey, and emptying it in `forgetIdentity` (gate backlog row 7).
- A `Clear-Site-Data: "storage"` header on the sign-out answer (identity's endpoint) would make the
  browser empty every store of the origin on Sign out, whatever a later screen puts there.

## Processor time this round adds

Measured on this machine, run alone: `browserReach.test.ts` about 9 s (one TypeScript program for
the product, then only the planted file is read again per plant), `deviceStores.test.tsx` about 7 s,
the e2e test about 5 s of browser time. About 21 s in all, against the 10,500 s maximum.

## Odoo comparison

Unchanged this round: find one user among 100,000 and open a record by keyboard were won on every
metric in round 14, with no tie and nothing left out. Contacts (p16) is not built yet.

## Verify runs this round (all through the verify slots)

1. Stopped by me after its web stage: the machine was at load 57 to 80 with three other verifies
   and gate runs outside the slots. 50 web tests failed, nearly all p04's shell journeys
   (clientIsolation, paletteActions, navigationReopen), which pass run alone at load 11; three were
   this round's own gates on their 5 s limit. That made this round cut their cost: every planted
   variant of the static gate is now read in one TypeScript program instead of one per plant, and
   the behaviour gate watches every change of the page instead of relying on step timing.
2. Web failed only on three comparison-harness controls (p01, frozen; the load sensitivity of gate
   backlog row 8), e2e failed on this round's new test (its Sign out locator also matched a
   record's "Sign out everywhere"; fixed with the exact name). .NET passed.
3. Passed, commit 0a84a8a8: slot taken 01:19:44Z, verify passed in 4,714 s, processor time
   7,515 s (dotnet 4,819, web 2,246, e2e 416, timing 34) against the 10,500 s maximum. .NET
   774/774, web unit 582/582, e2e 121/121, comparison harness 486/486, build 6/6.
