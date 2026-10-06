# p05 — A search field can be limited to Arabic script; the users list searches the Arabic name

Date: 2026-10-05. Piece: p05-list-search (round 4). Status: accepted.

## Context

p03 round 2 (`p03-identity-user-corrections.md`) moved a user's Arabic name into its own column,
`displayNameAr`, and kept it out of quick search: a third search field would add a third column to
every word's OR, and p03 measured that as doubling the search time over 100,000 users. Quick search
then no longer found "فاطمة الزعابي" by her Arabic name (p05's `UsersListTests` failed when round 3
was integrated), although Arabic search with the spellings people type is one of p05's strengths
against Odoo. Both constraints hold: the Arabic name must be searchable, and a Latin search must
not get slower.

## Decision

- `ListColumn` gets an optional `Script` (`ListTextScript.Any`, the default, or `Arabic`). A
  search field marked `Arabic` is tried only for search words that contain an Arabic letter
  (Arabic, Arabic Supplement, Arabic Extended-A and the presentation forms;
  `ListSearch.HasArabicLetter`). A Latin word cannot occur in Arabic-script text, so leaving the field out of its OR changes no
  result for that word and costs nothing.
- Fields of `Any` script (a name typed in either script, an e-mail address, which may be
  internationalised) are tried for every word. A definition is refused at start-up when every
  search field is limited to one script (some searches could never match) or a non-text column
  names a script.
- Relevance follows the same rule: each word's tests look at the fields that word can occur in,
  and the whole-search tests (equal, phrase start, typed order) at the fields every word can occur
  in.
- The users list adds `displayNameAr` as a third search field, marked `Arabic`. A Latin search
  still ORs `displayName` and `email` (checked by `ListScriptSearchTests` on the generated SQL); an
  Arabic word also tries `displayNameAr`, on its own trigram index.
- `setup` and `seed` end with `VACUUM (ANALYZE, SKIP_LOCKED)` as the owner role. Measuring this
  change showed that right after a 100,000-user seed every search waited on autovacuum: Arabic
  words took 1.6–2.6 s until autovacuum ran about a minute later, then 55–65 ms. After the vacuum
  step the demo is fast from its first request (the step takes under a second).

## Measured

`./erp up` with the shared dataset (100,004 users in the workspace), round trip over HTTP on the
owner's PC, five runs each, first request included after the vacuum step:

| Search | Rows | Time |
|---|---|---|
| `فاطمه` (teh marbuta typed as heh) | 3,372 | 83–237 ms (first), then 83–90 ms |
| `النعيمى` (yeh typed as alef maqsura) | 1,686 | 60–85 ms |
| `مريم` | 3,375 | 36–48 ms |
| `ا` (too broad to rank) | 93,127 | 169–203 ms |
| `maj ani pil` | 3 | 12–22 ms |
| `wa` | 9,783 | 110–122 ms |

## Why not

- *Keeping the Arabic name out of search* (p03's choice): "find one record among 100,000" by an
  Arabic name is a task UAE users do; filtering by a column is more keystrokes than typing.
- *One combined search column* (a generated `search_text` with one trigram index): a generated
  column in another module's table, and the relevance tests need to know which field matched.
- *A regular expression with letter classes in the WHERE clause* (one condition per field
  instead of one ILIKE per spelling): `~*` is not leakproof, so under row-level security it could
  not use the trigram index, and the leakproof set may only shrink (`g1.leakproofChanges`).

## Round 4 additions (2026-10-06)

- **Arabic-name search is timed at demo volume.** `UsersListVolumeTests` now times the Arabic name
  over 100,000 users within the same budget as the Latin searches (median under 400 ms, no run
  over 1 s): as written, with the spellings people type for one another, in any order, mixed with a
  Latin word, and broad Arabic searches (a common first name, one letter).
- **Names stored with short vowels or shadda.** A search word typed with marks ("مُحَمَّد") was
  tried only without them, so the name as stored with them was never found. Such a word is now
  also tried exactly as typed (first, within `MaxSpellings`), and the relevance patterns accept
  marks after every letter of an Arabic word, so "مُحَمَّد علي" still ranks as the exact match.
- **Shadda typed or not.** Names are often stored with a shadda on one letter ("شمّة", "محمّد",
  "عليّ") and typed without it. An Arabic word now also matches each of its spellings with a
  shadda after one letter (the first excepted), counted as one more change, with the letter
  variants first at equal changes and the whole set still bounded by `MaxSpellings` (32).
  `show_trgm('شمّة')` shows PostgreSQL's trigram extraction reads the shadda as part of the word,
  so these patterns are served by the trigram index like the others. Measured on the 100,000-user
  demo (PostgreSQL alone, three runs): "فاطمه" over three fields with its 10 letter spellings
  (30 patterns) 40–57 ms, with 32 spellings (96 patterns) 39–40 ms, with all 50 spellings 44–64 ms;
  the index scans take under 2 ms and the recheck stops at the first pattern a row matches.
  Before this, "شمة" found none of the demo's 1,055 users named "شمّة".
- **One regular expression per field before the LIKE patterns.** The planner drives the index
  from one word's patterns and rechecks every other word's on each candidate row: a row that does
  not match then costs one LIKE per spelling and field (96 for a word of 32 spellings). A word
  with several spellings is now first tested with one case-insensitive regular expression per
  field that accepts every spelling (letter classes, marks after any letter); the LIKE patterns
  stay, so the trigram indexes still find the rows and the result is exactly theirs. `~*` is not
  leakproof, which only matters for driving an index under row-level security; as a filter it runs
  after the tenant condition like any other. Measured over 100,000 users ("شمه المنصورى", 117
  rows; PostgreSQL alone): 82–124 ms with the LIKE patterns only, 40–46 ms with the expression
  first. Over HTTP on the demo with the machine shared by other agents (load 78–95 on 16
  threads), the two-word Arabic searches went from 270–380 ms to 100–280 ms.
- **Known limit.** A word typed without short vowels (fatha, damma, kasra, sukun, tanween) does
  not find a value stored with them ("محمد" does not find "مُحَمَّد"); they are rare in stored
  names, and matching them in general would need the value with its marks removed, which under
  row-level security can drive the trigram index only as a plain column (an expression such as
  `translate()` is not leakproof): a stored, normalised search column in the owning module's table
  (for users, p03's `identity.users`), left to that module.
