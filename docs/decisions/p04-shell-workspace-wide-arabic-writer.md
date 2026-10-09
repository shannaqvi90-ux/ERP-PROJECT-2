# p04 — A workspace-wide Arabic writer for records every company shares

Date: 2026-10-09. Piece: p04-shell. Status: accepted (round 7).

## Context

Round 6 of this piece could not be integrated. p02 round 7 (merged after this piece's last merge of
the integration branch) added `IWorkspaceWide`: the workspace record (`PUT /api/tenancy/tenant`) is
written only by someone who works in every company and every branch, otherwise the endpoint answers
403 `tenancy.workspaceNeedsEveryCompany` (and the kernel refuses the row as the second layer).

This piece's Arabic sessions (`ArabicSession`, `p04-shell-arabic-sessions.md`) sign in the seeded
Arabic administrator `admin.ar`. In the gate seed that user works in only one branch of the last
company (`TenancyAccessSeeder`, so every access table holds rows of both companies). Its Arabic write
of the workspace is therefore refused by design, and the G1 HTTP gate counted the refusal as "tenant
B's own writes must succeed" and "tenant A's own writes in the write-after-write phase must
succeed" (two each). The non-interference gate passed, but for that endpoint it only compared two
refusals, so the success path of the workspace write never ran in Arabic there either.

## Decision

- **A second Arabic writer, created when needed.** `TenantActivity` keeps `admin.ar` as its Arabic
  actor for every read and write. When `admin.ar`'s write is refused with a
  `workspaceNeedsEveryCompany` code (endpoint or kernel) and `admin.ar` indeed does not hold the
  whole workspace (`everyCompany` of `GET /api/tenancy/tenant`, read once), the same write (same
  record rules, same body variant) is made again in Arabic by an Arabic-speaking administrator who
  works in every company and every branch. That write is the one that must succeed: a failure is
  an unsuccessful own write, as before. Any other answer of `admin.ar` is judged exactly as before.
- **The user is made through the product's API** (`ArabicSession.CreateWorkspaceWideAsync`): the
  tenant administrator creates `admin.ar.workspace.<run>@<tenant domain>` with the administrator's
  roles and language `ar`, then grants every company the access record offers with every branch.
  Signing it in checks `everyCompany` is true and throws otherwise, so the gate cannot quietly run
  with a writer that is refused too. The user's names carry the tenant code and the activity's run
  tag, so they are that tenant's alone; writes never target it (it joins the actors' own user
  records). It is created only the first time an activity needs it, inside the framed own write
  when change tracking is on, so it never counts as a change made by someone else.
- **G1 HTTP gate**: the report carries the refusals and the workspace-wide writes; every refusal
  must be followed by a successful workspace-wide write, and `g1.arabicWorkspaceWideWrites` (new
  ratchet key) sets the least number of them.
- **Non-interference gate**: when either tenant's `admin.ar` write of an endpoint is answered with
  that refusal, the write is compared once more, both ways, between the two tenants'
  workspace-wide Arabic administrators (bearer tokens, shared process and fresh processes as for
  every other write). Each such true answer must be a success, or the check reports itself blind.
  New ratchet key `g1.writeNonInterferenceArabicWorkspaceComparisons`.

## Why not the alternatives

- *Accept the 403 for `admin.ar`.* The success path of the workspace write would never run in
  Arabic: the gate would be narrower than before p02 round 7.
- *Change the gate seed* (give `admin.ar` every branch, or seed another user). The gate seed is
  also the module tests' seed, and `admin.ar`'s branch limit is what fills the branch-access tables
  for p02's gates; changing either moves other pieces' tests.
- *Let the full administrator write in Arabic.* The request language follows the user's saved
  preference before Accept-Language, and switching that administrator's preference would turn the
  English writers Arabic too.
- *Replace `admin.ar` with the workspace-wide writer everywhere.* Every Arabic write would then run
  as a user of every company, and the Arabic writes in the last company's single branch (the
  branch-limited path) would no longer run. Keeping both only adds the writes that need it.

## Cost

Only the writes refused for want of the whole workspace are repeated (today one endpoint,
`PUT /api/tenancy/tenant`): in the G1 HTTP gate two Arabic writes per tenant in the write pairs,
plus one user created and signed in per activity that needs it; in the non-interference gate two
comparisons (about ten writes) and two users. Measured numbers are below.
