# G1 attack names and tenant-distinct example values (round 23)

Two false positives in the G1 HTTP isolation gate, fixed without weakening what it catches.

1. Counter names. Tenant A's attack wrote `attack-{n}` from a running counter; n reached numbers
   tenant B also holds, so A's own role or audit row read as B's value. Written names are now
   `attack-<per-run random letters>-<n in letters>` (AttackNames.Text): no digit run, and unique per
   run. The company gate's `company-attack-{n}` and the switch-input names follow the same rule.
2. Shared example values. The gate's valid writes take the API's example values, so both tenants
   created a company with the same name and a label derived from it (p10's sequence label
   "document type · company name") became a B marker that A legitimately holds. Tenant B's writes
   (TenantActivity) and tenant A's switch-phase writes now append their own tag to name fields that
   took the example (AttackNames.TagExampleNames), only where the longer text still conforms to the
   documented limits. As a second layer, a marker found in an answer is judged B's only if tenant A
   does not hold that text in its own rows at that moment (AttackerRows, superuser, asked only on a
   hit); an own value is scrubbed from the answer and the search goes on, so another B marker in
   the same answer is still found.

Self-tests (MarkerJudgeSelfTests): an own label is no leak; a planted leak of a derived label A does
not hold is caught, also beside an own value; a counter-named row of B is caught; written names carry
the token and no digits. Cost: one query per tenant table per found marker, only on a hit.

## Round 23 integration fix: the take of a list walk

Cause of the integrator's failure (share 4, `identity.roles` "no query was answered over more than one
page"): with names that can no longer collide, every one of tenant A's role writes succeeds, so A holds
1,607 roles to B's 25 (before, duplicate `attack-<n>` names made most writes fail). The walk's take is
ceil(larger total / 4), at most 199, so B's 25 roles fit one page and the blindness check fired. Not
the tagging of B's names.

Fix (tests/Erp.Gates.Tests/G1/ListAnswers.cs): while no query of a list has yet been answered over
several pages (keyset and offset, unsorted and sorted), a query whose judged tenant would get a single
page takes ceil(its own total / 4) instead, if the larger tenant's walk then stays within 300 pages.
Blindness findings now print the first five queries with totals, take and pages. Nothing is loosened:
the check still requires a multi-page walk of the judged tenant. The extra requests are a few queries per list.

Runs (alone, through verify-slot): share 4 before the fix failed (30 m 19 s); after it passed (36 m 45 s).
