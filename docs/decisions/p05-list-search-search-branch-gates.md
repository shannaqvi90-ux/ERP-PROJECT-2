# p05 round 13: the volume gate searches every branch of the search engine; file-system state is reviewed; every list write route is sent by a reader

Critic p05 round 12 (BLOCKED) planted three faults the gates missed or caught only by side effect:

- **L13** (G1): the total of a broad initials search (one word of 2 to 5 Latin letters, no filter,
  over 10,000 matches) kept for a minute in a file in the temporary folder, keyed by list and word
  without the tenant. All 68 G1 tests passed: the volume gate searched one-letter words only, so the
  initials branch and the broad-search branch never ran at volume, and no source rule looked at the
  file system.
- **P10** (G2, screens): the views menu offered a reader without `lists.views.share` "Save changes"
  and "Delete" on the workspace's shared view; every web unit test passed.
- **P11b** (G2): `POST /api/lists/{key}/views/{id}/share`, a shared copy of the caller's own view under
  the list's read permission, was caught only by the reviewed list of read-permission writes; the
  behavioural check sent a reader's writes to `POST /views` and `PUT /views/{id}` only, and the
  derived permission rule accepted any route under the list's prefix.

## Decisions

1. **Volume searches built from the engine's branches** (`tests/Erp.Gates.Tests/G1/ListVolumeBranches.cs`).
   `ListVolume.SearchBranch` names the branch `ListBinding` plans for a search: one letter (Latin or
   Arabic), a short word of 2..`MaxInitials` letters (the initials branch when the binding searches
   initials), a long word, an Arabic word, a word with digits, a word with other characters, several
   words (Latin or with Arabic). Each branch is judged twice, too broad to rank in both tenants and
   ranked in both, with true counts that differ between the tenants. Required: every short-word size
   2..5, long words, digits (when both tenants' rows hold digits), several Latin words, and, when both
   tenants hold Arabic text in the fields an Arabic word searches, an Arabic word (both sizes) and
   several Arabic words (ranked). A required branch without such a search is reported blind and fails
   the gate, as the ranked/unranked one-letter tallies already did.
   - The searches are chosen from the tenants' own rows, not hard-coded: pieces of the words in the
     search fields (Latin 2..8 letters, digits 1..4, Arabic 2..5 letters, single other characters),
     the initials of the binding's initials column, neighbouring word pairs and pairs of the commonest
     short pieces. A sample of 3,000 rows per tenant orders the candidates (broad: the narrowest still
     clearly broad; ranked: the broadest still clearly ranked, initials first), and the exact counts
     over every walked row decide (at most 12 tries per branch). So a later list (Contacts, p16) gets
     its own branch searches without anyone choosing words for it.
   - Truth: `ListVolume.SearchMatcher` matches every word (any spelling, `ListSearch.Spellings`) in a
     field `ListDefinition.SearchFieldsFor` gives it, any case, and a one-word 2..5-letter Latin search
     also by `ListSearch.InitialsPattern` on the initials column. To judge initials from the rows the
     list answers, the kernel's `IListBinding` gains two additive members with defaults,
     `SearchesInitials` and `InitialsColumn` (the bound column that reads the same member as
     `.Initials(...)`); a list whose initials read a value no column shows is reported blind.
   - The first 2-letter word of each size is also sent with a filter, a sort and a grouping (L13 keyed
     on "no filter"; a variant keyed on the filter would otherwise pass).
   - Branch searches run only on lists at volume (users and access), so the chosen searches and the
     tallies are the same on every run (a small list's digit search depends on the canary's digits).
   - Each branch query is sent B, A, B, A on its first page and its last rows (and past the ranking
     limit when broad), not the full five offsets of the other queries: the deep-offset code is
     already judged by every other broad query; this keeps the added requests to about 440.
   - Ratchet: new `g1.listVolumeSearchBranches` 31 (users 17, access 14); queries 285 to 328, answers
     2,916 to 3,348, discriminating 82 to 125, deep answers 840 to 1,016, unranked searches 16 to 33,
     ranked 7 to 23.
   - Chosen at this commit (A/B rows), users: `id` 12,530/12,909 and `oo` 7,950/8,203 (2 letters),
     `sha`/`man`, `ampl`/`ashi`, `ample`/`manso`, `exampl`/`hamdan`, digits `0`/`41`, `am ex`/`al suwaidi`,
     `ال` 19,870/20,458, `ري` 5,656/5,781, `ال ري`, other characters `.`.
2. **Self-test V4** in `VolumePlantModule`: every search other than a one-letter one keeps the first
   asker's total (broad or ranked, with or without filter, sort, grouping), in process memory (the
   check is black-box; where the plant keeps the total does not matter to it). The self-test asserts
   that each required branch, each size, is caught on its own, and the filtered variant. V2 and V3's
   assertions got narrower targets so V4's reports cannot satisfy them (V2: a filter without a
   search; V3: a one-letter search, the only searches V3 still plants).
3. **Source rule `external-state`** in `TenantBypassScanner` (G1 tenant source): file and directory
   statics, file streams, `FileInfo`/`DirectoryInfo`, `StreamWriter`/`StreamReader` construction, the
   temporary and special folders, memory-mapped files, named mutexes, semaphores and wait handles,
   pipes, isolated storage, the registry, file-system data-protection keys and `using` aliases of
   those types. The two existing uses are reviewed in `tests/Gates/tenant-bypass-sources.txt`
   (seeding reads the comparison dataset's CSV; exports write to the response stream and to zip
   entries). 19 planted shapes, L13's lines among them, in the scanner self-test. `g1.tenantSourceRules`
   13 to 14.
4. **Every write route sent by a reader** (G2 `ListViewChecksAsync`): a reader without
   `lists.views.share` sends every non-GET route under `/api/lists/{key}/` (any shape) with its own
   personal view's id in every route parameter and a full body; the workspace's views must not change
   and no new shared view may be listed to it. `g2.listReaderRouteWrites` 30 (5 lists x 6 routes).
   Self-test with `ShareCopyPlantModule` (P11b's handler as a real endpoint).
5. **Derived list routes are a closed set**: the permission rule derives a list route's permission only
   for the ten routes the lists module generates; any other route under `/api/lists/{key}/` fails
   until its shape and permission are added there or it is reviewed in a map.
6. **Views menu unit tests** (`ListView.test.tsx`, "saved views and the share permission"): a reader
   without the share permission, on the workspace's shared view with a change, is offered neither
   "Save changes" nor "Delete"; on its own view both; a sharer both on the shared view; the save
   dialog's share box only for a sharer. With P10 applied the first fails.

## Rejected

- Fixed search words per list: a later list's words would need choosing by hand, and a reworded
  seed would make the check blind silently; the blind report is what makes a missing branch fail.
- Judging the branch searches at all five offsets: about 300 more requests for code the other broad
  queries already judge deep.
- A separate planted host per branch in the self-test: one planted host with disjoint plants judges
  each branch in one run.

## Cost (processor time)

No new environment or test run: the branch searches ride on the volume fixture's existing walks and
on its two existing tests (main and self-test). Added requests: about 440 answers in the main test
(2,916 to 3,348) and the same queries again in the self-test, users only; candidate choice is a
sample count and at most a few dozen exact counts over the walked rows (well under a second). The
G2 check adds 30 requests and one planted host self-test (3 s). Measured alone in the toolbox image
under load 45-50: the volume main test 3 min 48 s, its self-test 2 min 5 s (round 12's critic: 3 min
12 s and 1 min 52 s under its load).

Full `./erp verify` of this branch (integration branch merged at 8e5fa749) through the verify slot:

- Run 1 (7dc67175, slot taken 08:42:04Z): every stage but .NET passed; .NET failed only on the new
  `g1.listVolumeAnswers` minimum, set to 3,352 from an estimate; the run gave 3,348 (the small lists
  judge no branch searches; every other figure and every chosen branch the same as the gate run
  alone). The minimum is now 3,348.
- Run 2 (539cb375, slot taken 09:54:56Z, 66 min wall): **every test passed** (.NET 657, web unit
  516, end-to-end 110, comparison 415, build 6) and the ratchet failed only on processor time:
  **10,615 s** (dotnet 8,046, web 2,112, e2e 396, timing 61) against the owner's maximum of 10,500,
  with the load at 26 to 38 from other agents' runs. Round 12's own verify used 9,901 s (dotnet
  7,574, web 1,942); the web stage, which this round changed by four unit tests only, rose by 170 s
  with the load, and the .NET stage by 472 s, of which this round's added requests are a small part
  (the volume tests ran 4 min 57 s and 2 min 55 s of wall time beside the other gate tests).
- Run 3 (242c64a4, slot taken 11:02:07Z, 3,999 s wall): again every test passed (the same counts)
  and only processor time failed: **10,657 s** (dotnet 8,141, web 2,129, e2e 324, timing 64), load
  31.5 at the start, 26.3 at the end, highest 77.5.
- What this round adds, measured: the verify counts the stage containers' processor time (cgroup;
  the test PostgreSQL containers are not in it). The three volume tests alone in the toolbox image,
  same machine, one after the other: integration commit 8e5fa749 **109.2 s**, this branch **110.8 s**
  (+1.6 s: the branch searches' requests and truth). The G2 additions are 30 requests and one planted
  host (about 4 s). So this round adds about 6 processor seconds; the rest of the rise from round
  12's 9,901 s comes with the integration branch's own growth (p04 recorded 10,338 s for 4f8c7790)
  and the load. No maximum was touched.
