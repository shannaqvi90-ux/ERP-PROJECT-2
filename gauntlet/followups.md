# Follow-ups of pieces accepted with minor gaps

These are worked in a later follow-up round and never dropped.

| # | Added | Piece | From verdict | Follow-up | Status |
|---|---|---|---|---|---|
| 1 | 2026-11-27 | p01-odoo-rig | p01-odoo-rig r16 (82611d1b) | Blind shots tell the products apart by lightness (ours dark, Odoo light): all 20 ours shots have brightness 13-28/255 and all 23 Odoo shots 121-242; render ours' blind shots in the light theme (erp.theme device key) or normalise lightness, then check the ranges overlap (blind-brightness.txt, plants/blind-brightness.mjs, screenshots 01/02/05/06). | Open |
| 2 | 2026-11-27 | p01-odoo-rig | p01-odoo-rig r16 (82611d1b) | Task fixture user name 'Layla Linguist' is unmasked in ours' switch-to-arabic shots but masked in Odoo's; add fixture user names (TESTER.name in drivers/*/switch-to-arabic.mjs) to lib/blind.mjs identity lists or mask the signed-in user element by selector (03-switch-arabic-done-x.jpg vs 04-switch-arabic-done-y.jpg). | Open |
| 3 | 2026-11-27 | p01-odoo-rig | p01-odoo-rig r16 (82611d1b) | review.html keeps only the last invocation's tasks when tasks are run one at a time into the same --out (only add-rate shown while comparisons holds 8 files); have writeReview (lib/review.mjs) build the page from every comparison in <out>/comparisons using key.json letters (compare/blind/review.html, compare/comparisons, key.json). | Open |
