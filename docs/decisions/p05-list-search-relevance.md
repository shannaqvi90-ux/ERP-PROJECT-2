# p05 — Quick search lists the best match first, and reads Arabic the way people type it

Date: 2026-10-03. Piece: p05-list-search (round 2). Status: accepted.

## Context

Round 1's critic found that quick search ranked nothing: results kept the list's default sort
(`-createdAt`), so "yousef wang" returned 46 rows with the wanted user anywhere, and finding one
user among 100,000 took as many keystrokes as in Odoo (the full name, then Enter). Arabic search
did not match the spellings people use for one another ("فاطمه" did not find "فاطمة", "الزعابى" did
not find "الزعابي").

## Decision

**Relevance order.** A request with `search` and no `sort` is ordered by relevance, then by the
list's default sort, then by the row id. Relevance is one whole number computed by the store in the
same query that pages the rows (and returned with each row, so the keyset cursor carries exactly the
value PostgreSQL compared; it is never recomputed in .NET):

| Part | Score |
|---|---|
| each word at the start of a search field | 4 |
| otherwise, each word at the start of a word inside a field (after a space or a dot: a last name, an e-mail's parts) | 2 |
| the whole search equal to a field | 16 |
| the whole search at the start of a field (several words) | 8 |
| the words in the typed order, the first at the start and each next at a later word start (`yous% wang%`) | 4 |

Each test is one case-insensitive regular expression per search field (`~*` in PostgreSQL, the
same pattern in .NET for in-memory lists): `^word`, `[ .]word`, `^w1 w2$`, `^w1 w2`, `^w1.*[ .]w2`.
Letters and digits are kept, every other character is escaped, and each Arabic letter of a spelling
group becomes a class of its group (`[اأإآٱ]`), so one expression covers every spelling. (A first
version used one `ILIKE` per spelling and test: eight per row for a one-letter search, 1.3 s over
100,000 users on the busy build machine; the expressions cut that per-row cost several times.)
The score is multiplied by 1,024 and the length of the first search field (capped at 1,023) is
subtracted, so among equal scores the shorter value, the closer to what was typed, comes first.
Matching itself is unchanged: every word must occur anywhere in a search field, so the trigram
indexes still serve the WHERE clause; relevance only orders. In cursors the order is the sort key
`-~relevance` (a name no column can have), so a cursor of the relevance order is refused for an
explicit sort and the other way round. An explicit `sort` always wins.

**Bound.** Only a search matching at most 10,000 rows is ranked (`ListSearch.MaxRankedRows`); a
broader one (a letter or two over 100,000 users) keeps the list's default order, because ranking
every row costs a scan with several pattern tests per row and tells nothing. The page says which
order it used (`ranked`), and a cursor keeps the order of its first page even if the count crosses
the bound meanwhile. Measured on the 100,000-user demo on the shared build machine (load average
above 30): "maj ani pil" 30–100 ms, "mariam kho" 25–190 ms, "maj" (3,265 rows, ranked) 70–200 ms,
"wa" (9,783 rows, ranked) 0.3–0.7 s, "فاطمه" 50–100 ms; "a" unranked as before.

**Arabic spellings.** Each search word expands to the spellings it matches: the alef forms
(ا أ إ آ ٱ), yeh and alef maqsura (ي ى ئ), teh marbuta and heh (ة ه), waw and waw with hamza
(و ؤ); short vowels, shadda, sukun and tatweel typed in the search are dropped. The first and last
letters (where spelling varies most) are expanded first, up to 32 spellings per word; the word
matches when any spelling occurs (`ILIKE` alternatives, each served by the trigram index).

**Screens.** The list sends no `sort` while the user searches without having chosen a sort (header
click or column menu); the top row is marked and Enter in the search box opens it. The count shows
"best match first". Choosing a sort from a header keeps it while searching (and in the address).

## Why not

- *Folding the stored text* (a function or generated column, `translate(...)`, indexed): under
  row-level security a condition can drive an index only when every function in it is LEAKPROOF.
  The platform keeps the set of leakproof functions reviewed and capped (`g1.leakproofChanges`
  maximum, which may only go down), and a generated column per searched field would change other
  modules' tables. Expanding the typed word keeps the database untouched and the plan indexed.
- *`ts_rank` / full-text search*: names and e-mails are not prose; prefix and word-start matching
  on short fields is what finds people, and it needs no dictionary per language.
- *`similarity()` ordering (pg_trgm)*: it rewards character overlap, not the start of a name part,
  and ranks "Wang Yousef" with "Yousef Wang" for "yousef wan".
- *Ranking in the browser*: only the loaded page would be ranked; paging would not follow it.

## Consequences

- A ranked search costs one sort over at most 10,000 matching rows with a few regular expressions
  per row; broader searches cost what they did before.
- Search fields stay plain columns with trigram indexes (the index gate is unchanged).
- The find-user comparison types the first letters of each part of the name ("Maj Ani Pil") and
  opens the user from the few rows that match: the search matches words anywhere, in any order, and
  ranks the best first, which Odoo's contiguous-substring search cannot do.
