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
