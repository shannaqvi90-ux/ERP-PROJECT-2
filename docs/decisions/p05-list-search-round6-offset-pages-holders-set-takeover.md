# p05 — Round 6: offset pages under G1, framework singletons as state roots, set-based takeover under G2

Date: 2026-10-08. Piece: p05-list-search. Status: accepted.

Round 5's critic planted two bugs the gates missed. L10: the list engine took a per-query scratch
object from an `ObjectPool<ListPageScratch>` (a DI singleton served by the framework's
`DefaultObjectPool<T>`), remembered the first page's total in it keyed without the tenant, never
cleared it, and handed that total to offset (`skip=`) pages. P5: the "never change a user who holds a
permission the caller lacks" check was removed from `POST /api/identity/users/matching/active`, and
a user manager deactivated the Administrator.

## Decisions

1. **Every list query is also walked by offset (G1 ListAnswers).** After the keyset walk, both
   tenants walk the same query by `skip` in lock step (judged tenant's page n, then the other's). Each
   offset page's total and groups are judged against the keyset walk (the tenant's own rows, checked
   by id against the database), and the offset pages together must return exactly the keyset rows.
   A list with no query answered over more than one offset page is a blind spot and fails the gate.
   Ratchet: `g1.listAnswerOffsetPagesJudged`. Reason: the web client sends `skip=` when a user
   jumps through a long list (`useListRows.ts`), so offset paging is a product path like keyset.
2. **Non-interference sends offset pages too.** For every registered list: `skip` with each sort,
   each search text, each grouping. Before judging a request with `skip`, the other tenant sends
   that query's first page and then the same offset page (a jump into a list the other tenant has
   just opened).
3. **Framework singletons over product types are roots of process state.** A singleton whose
   service or implementation type is a framework type built over a product type at any depth of
   generic arguments (an object pool, a cache, a channel) or a bare framework collection is
   reported as `framework-singleton <type>` and must be reviewed in
   `tests/Gates/process-state-allowlist.txt`. It is also walked as a root, framework internals
   included (up to six framework levels), down to the product objects it holds, which are judged
   field by field, and it is fingerprinted before and after the G1 HTTP attack. The five options
   registrations the product has today (`IConfigureOptions<…>` and friends over
   `SessionAuthenticationOptions` and `AuthOptions`) are reviewed. Ratchet:
   `g1.frameworkSingletonsInspected` (5). This is additive to p00's process-state inspector:
   `ProductContext` gains `FrameworkHolders`, the walk gains `ReachableState.FrameworkHolder`; the
   existing roots, findings and keys are unchanged, so p00's version of the same idea can merge into
   one mechanism.
4. **The tenant-source scanner's `process-global` rule names pools and caches.** Object pools and
   their providers, `ArrayPool<>`, `MemoryPool<>`, `IMemoryCache`, `IDistributedCache`,
   `HybridCache`, `IOutputCacheStore`, `Add…Cache…()` registrations, `AsyncLocal<>` and
   `ConditionalWeakTable<>` are process-wide stores outside product fields. No file under `src/`
   uses one today; a new use must be reviewed.
5. **G2 takeover covers set-based changes.** Every write without a route id whose body selects rows
   the way a list does (a `search` or `filter` field) under the users list's route is found from
   the running app and its API description. A caller holding exactly that endpoint's permission
   aims the selection, by search and by filter, at exactly one stronger user (the Administrator and
   every `GrantTargets` shape), with every combination of the body's flags and the list's own count
   in count fields: the stronger user must stay exactly as they were. The same requests aimed at a
   user without roles must change that user (the control). A set-based write on another identity
   route fails the gate until the gate learns that kind of record. Ratchets:
   `g2.takeoverSetEndpointsChecked`, `g2.takeoverSetTargets`.
6. **Gate self-tests plant L10's shape.** `LeakyModule` registers `leaky.jump`, whose offset pages
   reuse a total kept in a pooled `JumpScratch` (an `ObjectPool<JumpScratch>` singleton). The self
   tests require ListAnswers to report the judged offset page 2 in both directions and nothing on
   first or keyset pages, non-interference to report the offset pages in both directions, the
   process-state inspector to report the framework singleton, and the fingerprint to see the pool
   change under traffic.

## Product changes in the same round

- **The app's own confirmation.** Bulk actions declare `confirm` (plural messages for chosen rows and
  for all that match); `ListView` asks in `ConfirmDialog`, a modal `alertdialog` in the user's
  language and direction, with the confirming button focused (Enter confirms), Escape and Cancel
  cancelling, Tab kept inside, and focus returned to the opener. Deleting a saved view uses the same
  dialog. `window.confirm` is gone from the list framework.
- **Chosen-row bulk changes ask first too**, and run six single-user saves at a time instead of one
  after another (each still the same `PUT` with every rule of a single edit).
- **Boolean columns name their values.** `ListColumn` gains `TrueLabelKey` and `FalseLabelKey`
  (both or neither, boolean columns only; the definition check refuses anything else and the string
  gate requires both keys in English and Arabic). Cells, group headings, filter chips, the filter
  editor, prints and exports use them. The users, companies and branches status columns say
  Active/Inactive instead of Yes/No.

## Evidence of the gates catching the round-5 plants

- L10 applied to this branch: `G1TenantSourceTests` fails on `ListBinding.cs` and `ListsModule.cs`
  (`process-global`), `G1ProcessStateTests` fails on
  `framework-singleton Microsoft.Extensions.ObjectPool.ObjectPool<Erp.Kernel.Lists.ListPageScratch>`.
  The G1 HTTP isolation and non-interference runs with L10 are described in the round's notes.
- P5 applied to this branch: the set-based takeover check fails with the Administrator changed.
