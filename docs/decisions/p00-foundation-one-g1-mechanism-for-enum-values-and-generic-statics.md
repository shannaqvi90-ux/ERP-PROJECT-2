# One G1 mechanism for enumerated values and for generic-type statics

Piece: p00-foundation, round 7. Date: 2026-10-08.

## Context

Two verdicts named blind spots in the shared tenant-isolation machinery, which p00 owns:

- **p04-shell round 4 (plant L1).** Every body the gate built for a write took the first value the
  OpenAPI document publishes for an enumerated field (`OpenApiDocument.BuildBody` with documented
  values), and `TenantActivity.OwnLeaf` wrote `en` and `latn` for language and digits. Code behind
  any other value (the Arabic side of the preferences write) was never run by either tenant, so a
  leak there passed every gate.
- **p05-list-search round 4 (plant L6).** `ProcessState.Roots` skipped every type with
  `ContainsGenericParameters`, so the static fields of `ListBinding<T>`-style generic types were
  never roots, and a delegate-typed static field was taken as immutable although its closure can
  hold a dictionary shared by every tenant.

Both pieces fixed these on their own branches (p04 4f2f85f, e2b6102; p05 3c6e015), which are not
integrated yet. The lead asked for one mechanism, in the shared machinery.

## Decision

p00 takes those fixes into the shared machinery as they were written, not a second version:

- `OpenApiDocument.EnumLeaves` and `SetLeaf`, `TenantActivity.VariantsOf` (each documented value of
  each enumerated field alone, plus every field at its n-th value together), the write pairs of
  the HTTP attack and the write comparisons of the non-interference check over every variant,
  query parameters with an enumeration read with each value, leaky bugs 45 and 46 and their
  self-tests (p04 4f2f85f and e2b6102, gate files only).
- The report engine fix the variant writes made necessary (p04 20926c5, whole): a long unbroken
  cell no longer keeps a PDF request busy for good.
- `GenericStatics` (closed instantiations of product generic types named anywhere in product code
  are roots; a reached generic object adds the statics of its type, its generic bases and the
  generic types it is nested in), static delegate fields as findings, leaky bug 52 and its
  process-state assertions (p05 3c6e015, process-state parts only).

Ratchet keys `g1.enumValuesAttacked`, `g1.enumVariantWritePairs`,
`g1.writeNonInterferenceComparisons`, `g1.writeNonInterferenceEndpoints` and
`g1.writeNonInterferenceVariants` are added at the counts this branch measures.

## Why the same code rather than a p00 variant

Two mechanisms for one blind spot would each have to be judged, kept and ratcheted, and merging
the pieces would put both into the gate suite. The code here is the code on the p04 and p05
branches, so when those branches are merged the shared hunks are the same change; where a piece
has since gone further on the same lines (p04's Arabic sessions, p05's keyset-page list answers),
the integrator keeps the piece's later, stricter version.

## Left to the pieces

- p05's keyset-page judging in `ListAnswers` and its crossed view ids in G2 are list-search scope
  and stay on p05's branch; this branch carries only the process-state half of 3c6e015, and its
  self-test asserts only what that half catches.
- p04's Arabic sessions (an administrator who works in Arabic with Arabic-Indic digits) build on
  the variants and stay on p04's branch.
