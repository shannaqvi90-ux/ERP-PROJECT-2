# p03 identity: users are found by the initials of their name

Round 8, routed from critic p05 round 7 (find-user: every committed path of ours lost keystrokes
to Odoo's shortest name-only path, "il pi" plus a suggestion click and a row click, 5 keys; only a
row click after "m pi", 4 keys, won, by one key and 0.13 s).

## Decision

- A one-word search of 2 to 5 letters (no digits, no Arabic letters) also matches a value whose
  words start with those letters, in order, one word per letter: "map" finds Majid Anil Pillai
  (and Mona Al-Pillai: words end at spaces and hyphens). Opt-in per list binding
  (`ListBinding<T>.Initials(row => row.Name)`, kernel, additive: lists without it are unchanged);
  the users list names the display name.
- Ranking: an initials match scores 1, below a word at the start of a word (2): the people whose
  names hold the word still come first ("ali" lists people named Ali before Ahmed Latif Ibrahim).
  Among initials matches the shorter name comes first, as for every equal score.
- Matched on the value itself, nothing stored. The search for such a word becomes two
  conditions that together say exactly "the word is in a search field, or these are the
  initials": the first holds only case-insensitive LIKE tests (the word anywhere in a field; or
  the value starts with the first letter and a word starts with each next one after a space or a
  hyphen), which the trigram index serves and row-level security lets it use (LIKE leaks nothing);
  the second adds the regular expression `^m[^ -]*[ -]+a[^ -]*[ -]+p[^ -]*$` (case-insensitive),
  read only on the rows the first leaves.
- The search box says so: "Search by name, initials or e-mail" / "ابحث بالاسم أو الأحرف الأولى
  أو البريد الإلكتروني".
- Arabic names are not matched by initials: Arabic search words keep the Arabic search fields'
  own rules (letter variants, marks).

## Why not a stored, indexed initials column (tried this round, and dropped)

A stored generated column (`name_initials`, B-tree on tenant and initials) was the first design.
The full verify showed why it is wrong here: the G1 HTTP attack sends every text value of tenant
B's rows through every parameter, as tenant A, and compares the answer with one for a value that
exists nowhere. Initials are two to five letters that occur inside ordinary words and addresses
("anc" in tenant A's "Branch", "ac2" inside A's hexadecimal addresses), so tenant A's own rows
answered differently and the gate reported an oracle. Narrowing that comparison is not allowed
(CLAUDE.md rule 9), and a column that only serves an equality lookup is not worth a weaker gate.
A SQL function with an expression index was ruled out too: under row-level security a qualifier
calling a function that is not LEAKPROOF cannot use an index, and marking one LEAKPROOF is a
bootstrap change behind a human gate (needs-human #6).

Other options not taken: subsequence ("fuzzy") matching cannot be indexed at 100,000 rows; tuning
the comparison driver to a lucky fragment was ruled out by the lead (the initials path is computed
from the name alone, the same for every user).

## Comparison driver

`drivers/ours/find-user.mjs` gains the variant `initials`: Users, the initials of the name, then
Enter when the user is the best match (checked in set-up through the API), otherwise a click on
the user's row, after a counted scroll when the row is not on screen (bounding boxes of the row,
the grid and the window; no page script). On the shared dataset "map" matches 54 users and Majid
Anil Pillai is the 15th (shorter names first). Run on this machine against the shared Odoo rig
(both products, blind; ours started with `ERP_SEED_USERS_CSV`): `initials` verified at 3 steps,
3 keystrokes, 0.68 s of machine time (measured with the stored-column design; the query now
takes about 15 ms more at 100,000 users); Odoo's shipped best is 6 steps, 20 keystrokes, 2.85 s (the
round-7 critic's fragment path for Odoo: 5 keystrokes, about 1.2 s).

## Measured

100,004 users (`UsersListVolumeTests`): "swar" for Shamma Waleed Al Romaithi, median 37 ms,
slowest 49 ms (the same request with the regular expression alone read every row: 341 ms).
