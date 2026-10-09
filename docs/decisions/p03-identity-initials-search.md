# p03 identity: users are found by the initials of their name

Round 8, routed from critic p05 round 7 (find-user: every committed path of ours lost keystrokes
to Odoo's shortest name-only path, "il pi" plus a suggestion click and a row click, 5 keys; only a
row click after "m pi", 4 keys, won, by one key and 0.13 s).

## Decision

- A one-word search of 2 to 8 letters (no digits, no Arabic letters) also matches the stored
  initials of the list's first search field: "map" finds Majid Anil Pillai. It is opt-in per list
  binding (`ListBinding<T>.Initials(row => row.Initials)`, kernel, additive: lists without it are
  unchanged) and served by equality, so it costs one index probe beside the trigram scan.
- Ranking: an initials match scores 1, below a word at the start of a word (2): the people whose
  names hold the word still come first ("ali" lists people named Ali before Ahmed Latif Ibrahim).
  Among initials matches the shorter name comes first, as for every equal score.
- Identity stores them as `identity.users.name_initials`, a stored generated column
  (`regexp_replace(lower(btrim(display_name)), '([^[:space:]-])[^[:space:]-]*[[:space:]-]*', '\1', 'g')`:
  the first letter of every word, words ending at spaces and hyphens), indexed on
  `(tenant_id, name_initials)`. The application never writes it; bulk seeding and imports fill it
  by the database; a rename moves it. The audit trigger leaves it out of the change set (the name's
  change is already recorded), like `company_role_count`.
- The list index gate checks a binding's initials like a sortable column: a B-tree index leading
  with `(tenant_id, initials)`, or a problem (a computed initials value is refused).
- The search box says so: "Search by name, initials or e-mail" / "ابحث بالاسم أو الأحرف الأولى
  أو البريد الإلكتروني".
- Arabic names are not matched by initials: Arabic search words keep the Arabic search fields'
  own rules (letter variants, marks), and initials of Arabic names are not a habit we know of here.

## Why not something else

- A hidden list column for initials would show in the column chooser and filters of every
  users screen; a binding-level value keeps the list contract unchanged.
- Subsequence ("fuzzy") matching would find more but cannot be indexed at 100,000 rows.
- Tuning the comparison driver to a lucky fragment was ruled out by the lead: the initials path is
  computed from the name alone, the same for every user.

## Comparison driver

`drivers/ours/find-user.mjs` gains the variant `initials`: Users, the initials of the name, then
Enter when the user is the best match (checked in set-up through the API), otherwise a click on
the user's row, after a counted scroll when the row is not on screen (bounding boxes of the row,
the grid and the window; no page script). On the shared dataset "map" matches 54 users and Majid
Anil Pillai is about the 15th (shorter names first): 3 keystrokes and 3 steps,
against Odoo's 5 keystrokes and 6 steps.

## Measured

100,004 users (`UsersListVolumeTests`): "swar" for Shamma Waleed Al Romaithi, median 22 ms,
slowest 33 ms; the plan uses `ix_users_tenant_id_name_initials` and no sequential scan.
