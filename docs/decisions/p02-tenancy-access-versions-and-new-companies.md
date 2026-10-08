# p02 — Versioned access saves, new companies for the workspace's administrators, company edits need every branch, branch names start with the company's

Date: 2026-10-05. Piece: p02-tenancy. Status: accepted (round 4).

## 1. A user's company access is saved with the version read

The wave 1 integrity check found that `PUT /api/tenancy/access/{userId}` carried no version: two
administrators editing one user's access overwrote each other silently, while every other update
in the product answers 409 to a stale version.

- `GET /api/tenancy/access/{userId}` returns `version`: a hash (first four bytes of SHA-256, as an
  unsigned 32-bit number like every other version) over the user's company and branch access rows
  **in the caller's scope**, each with its row version (`xmin`). Any insert, change or delete of
  one of those rows changes it. Rows in companies the caller cannot see do not take part, so the
  version tells a one-company administrator nothing about the user's access elsewhere, and two
  administrators of different companies editing one user's access in their own companies do not
  conflict (each save changes only the caller's companies).
- `PUT` requires `version` (400 when missing) and answers 409 `concurrency` when it differs from
  the version computed now. Order: validation, the user exists, the lock (below), the grant
  refusals (403), the version (409), then the change. A refused caller learns "not yours" before
  "stale".
- **One save at a time per user.** A version compared and then written is not enough when two
  saves arrive together. The save first updates the user's row of `tenancy.user_company_totals`
  (creating it when missing), which holds its row lock until the request's transaction ends: the
  second save waits, then reads what the first wrote and is refused as stale. Two first saves of a
  user nobody gave access yet race on creating that row; the loser gets a unique violation, which
  the kernel answers as 409. The totals row is not company-scoped (tenant row-level security only),
  so the lock does not depend on the caller's companies. Everything is rolled back with a refused
  request.
- The access screen sends the version it read. On 409 it says another administrator changed the
  user's access after it was opened and offers **Reload** (English and Arabic), which replaces the
  draft with what is saved now.
- Gates: the grant gate reads the version as each caller before each request (so a refusal is the
  grant check's, never a stale version's), requires a stale version to be refused with 409 and to
  change nothing, and reports any grant endpoint read at its own route whose save carries no
  version. The company and branch attacks send the version they read in their direct grant
  attempts and require the refusal to be about the victim (`unknownIds`,
  `tenancyAccessBranchOfOtherCompany`), not about a missing version. The G1 HTTP attack's own writes
  copy the GET's body, version included.

## 2. A new company goes to the workspace's other administrators

Round 3 critic: only the creator of a company got access to it, so after the second administrator
created a company, the tenant Administrator no longer worked in every company, could not create
companies, and in a polluted run could no longer be managed by anyone.

Now the new company (all branches) also goes to every user who worked in every company before it
**and holds at least the creator's permissions**: the creator's peers and seniors. Users who hold
less (a clerk who happens to work in every company of a two-company group) are not given it
automatically; the creator gives it on the access screen. A one-company administrator gains
nothing. This keeps "every company" meaning the whole workspace for the people who run it, without
widening anyone's reach beyond what an administrator of every company could give them anyway.

## 3. Changing the company itself needs every branch of it

Round 3 critic: an administrator limited to one branch could still change the whole company record.
The company record (legal names, trade licence, tax registration number, base currency, fiscal
year, address) and its logo are shared by every branch, so changing them now needs every branch of
the company (`403 tenancy.companyNeedsEveryBranch`), as changing a branch code already did. The
company's GET returns `everyBranch`; the form shows it read-only with the reason, and the screens
gate has a case and two plants for it.

## 4. A new branch's English name starts with its company's

A UAE branch trades under its company's name followed by its own ("Falcon Logistics LLC - Jebel Ali
Branch"). The company form's branch line now starts with `<company legal name> - `, the caret after
it, so the user types only the branch's own part. Left at the prefix alone, the name saved is the
company's name. Any other name is typed over it (select all, type). This also took the
create-company-branch comparison from 72 keystrokes (a tie with Odoo, which is a loss) to 44 on our
side: the branch's own part is what the user actually has to say.

## 5. Fields follow their text's direction

A text field with no direction of its own takes `dir="auto"` once it has text, so an English trade
licence authority on an Arabic screen reads from its start instead of being clipped (round 3
critic); empty, it follows the screen.
