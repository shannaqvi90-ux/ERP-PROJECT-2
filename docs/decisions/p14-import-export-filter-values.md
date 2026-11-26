# p14-import-export: the Tags filter lists the tags in use

Round 3, after round 2's critic found that the zero-typing tag button only works when a row carrying the tag is on the first screen
(on the 100,000-contact comparison data the first "Government" row is about the 139th by name; the grid renders 32 rows).

## Decision

- `GET /api/contacts/contacts/tags` (permission `contacts.contacts.read`, the same as the list) returns the tags the caller's workspace
  holds, each once, most used first, at most 200. One grouped pass over the stored tag texts, so cost follows the number of distinct
  tag combinations, not contacts. Row-level security keeps it to the caller's workspace.
- The shared list kernel gets an optional `filterValues` prop on `ListView` (column key to an async list of values). A text column's
  filter editor then shows the values as a pick list; typing narrows it; one click applies `contains <value>` and closes the editor.
  It is additive: no column without the prop changes. Strings `lists.filter.values` and `lists.filter.noValue` exist in English and Arabic.
- The contacts screen passes it for `tags`. The tag button in a row stays.

## Why this path (comparison task export-filtered-list)

Path now: Contacts > Tags column options > Filter... > click the tag > Print or export > Excel = 6 steps, 0 keystrokes, independent of
which rows are on screen. Odoo needs 9 steps and 11 keystrokes on the same data. Paths tried and rejected in rounds 1 to 3: typing the tag
in the filter (8 steps, 11 keys, a tie on keys); tag button in a row (fails on the comparison data); list search (does not match tags);
column sort (the menu has none for tags). The pick list needs no typing, so it wins on steps and keystrokes.

## Cost

One endpoint (G2 map line in tests/Gates/endpoint-permissions/contacts.txt); a module test
`The_tag_choices_list_each_tag_of_the_workspace_once_and_never_another_workspaces`; a ListView unit test. No new dependency.

## Round 3 fix after the integrator's alone verify (serial-25b)

- The comparison health check judged export-filtered-list invalid: the pick list arrived by an async fetch after the click that opened the
  filter editor, a render from a task the harness could not trace to a step. The list now fetches each `filterValues` source as it opens
  (in a ref, no render when the answer lands) and gives the editor the values it already holds, so the pick list is in the commit of the click.
  A refresh that finds the same values changes nothing on screen.
- Run live on a stack seeded with gauntlet/compare/data/out: `node run.mjs --task export-filtered-list --product ours` = verified, 6 steps,
  0 keys, human 15.9 s, machine 1.378 s.
- The dotnet failure (G1ListVolumeTests, "could not resize shared memory segment ... No space left on device") was Docker's 64 MB /dev/shm in the
  test database container. tests/Erp.Testing/TestDatabaseServer.cs now sets a 1 GiB /dev/shm (a ceiling on a memory mount, not memory taken up front).
  This is p00's file; the change is one parameter. Targeted run: Erp.Gates.Tests (G1ListVolume, G2Permission, G1TenantSource, OpenApi,
  G2SetBasedWriteSource, ListContractGate) 48 passed in 24 m 43 s; Contacts.Tests 48 passed; web unit tests for kernel/lists and contacts 126 passed, tsc clean.
