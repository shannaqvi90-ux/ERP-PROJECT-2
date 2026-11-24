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
