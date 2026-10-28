# p10 — Document numbering: the numbering module

Date: 2026-10-25. Piece: p10-numbering, round 1. Status: accepted.

## What it is

A module `numbering` (schema `numbering`) with three tenant- and company-protected tables (row-level
security forced for the tenant and the company, the company write policies, the audit trigger):

- `sequences`: how one company numbers one kind of document: prefix, digits (1 to 12), suffix, when
  the numbers start again (`never`, `yearly`, `monthly`), whether they may skip (`gapless`), whether
  each branch counts on its own (`per_branch`). One row per company and document type (unique on
  `tenant_id, company_id, document_type`).
- `counters`: the counter of one sequence in one period (`period_year`, `period_month`: 0 and 0 for a
  sequence that never starts again, 2026 and 0 for a year, 2026 and 10 for a month; the API shows
  `""`, `"2026"`, `"2026-10"`) and, for a sequence counted per branch, one branch: the value the next
  number gets and the last one given. Unique on `tenant_id, sequence_id, branch_id, period_year,
  period_month` with `NULLS NOT DISTINCT` (PostgreSQL 15+), so "no branch" is one counter.
- `issued_numbers`: every number given, with its document type, period, value, text, document date,
  the document it went to (`source_id`) and whether it was taken gapless. Unique on
  `tenant_id, company_id, document_type, number`.

Periods and the reset are kept as numbers (`reset` is a `smallint`: 0 never, 1 yearly, 2 monthly),
not words: the report gate treats every stored text of the workspace as its data, and a period
"2026" or a word such as "never" stored as text read as data wherever a print shows the year or
the word (the first builder verify's finding).

## Decisions

1. **One sequence per company and document type, made with the company.** The seeder gives every
   company of every workspace a sequence for each registered document type (idempotent; a type
   registered later reaches every company at the next seed), and a company created through the API
   gets them in the same transaction through a new tenancy contract, `ICompanyCreatedHandler`
   (tenancy calls every registered handler after it saved the company and its creator's access).
   Why not a create endpoint: every company always needs one, the screen has nothing to create, and a
   unique create would answer 409 to the gates' repeated documented bodies (p08's finding). Why not
   rows made on first use: a branch clerk saving the first invoice could not write a sequence (every
   branch shares it, below), and the list gates judge every row a list answers against the database.
2. **Every branch of the company shares a sequence** (`ICompanyWide`): only someone who works in every
   branch of the company, holding `numbering.sequences.update` there, changes it; the endpoint
   refuses first (`numbering.companyNeedsEveryBranch`, `permissionNotHeldInCompany`) and the kernel
   refuses the write whatever the endpoint checked. Counters and issued numbers are company data
   anyone saving a document of the company writes (`ICompanyOwned`).
3. **Per branch is an option of the company's sequence, not a sequence of its own.** A sequence
   counted per branch keeps a counter per branch and must show the branch's code (`{BRANCH}`), or two
   branches would give the same number. Branch codes come from a new tenancy contract,
   `IBranchDirectory` (the branches the caller may see, through tenancy's own company and branch
   filters).
4. **Patterns.** Letters A to Z, digits and `- / . _ #`, with the tokens `{YYYY}`, `{YY}`, `{MM}` and
   `{BRANCH}`; at most 30 characters each side. A sequence that starts again every year must show a
   year, one that starts again every month a year and `{MM}`, or numbers would repeat. A wider value
   keeps all its digits. The same rules run on the server (`NumberPattern`, in the contracts) and in
   the screen's live preview (`web/src/modules/numbering/pattern.ts`); one file of shared examples
   (`tests/Erp.Modules.Numbering.Tests/pattern-examples.json`) holds both to the same results.
5. **Taking a number** (`IDocumentNumbers.TakeAsync`, called by later modules inside the unit of work
   that saves their document). One statement takes the counter's value and moves it on
   (`INSERT … ON CONFLICT (…) DO UPDATE SET next_value = next_value + 1 RETURNING`), which locks the
   counter's row until its transaction ends; a second records the number (`ON CONFLICT DO NOTHING`,
   refused with `numbering.numberTaken` when a changed pattern would give a number again).
   - **Gapless**: both statements run in the caller's own transaction. A second document of the same
     sequence waits (up to 10 s, then `numbering.busy`) until the first commits or rolls back; a
     rolled-back document gives its number back, so the numbers that exist run on with no gap, in the
     order the documents were saved. Proven against PostgreSQL with 24 documents at once, a third of
     them abandoned (`TakeTests`).
   - **May skip** (the default except for the tax documents and journal vouchers): both statements run
     in a short transaction of its own on a second connection, committed at once, so documents never
     wait for each other; a document whose save then fails leaves its number unused. The second unit
     of work is bound to the caller's own session values (tenant, actor, actor kind, correlation id),
     narrowed to the one company of the number, with the caller's branch limit and the permission's
     company limit; a read-only request is refused before it (reviewed in
     `tests/Gates/tenant-bypass-sources.txt`). Why not PostgreSQL sequences (`nextval`): one per
     counter would mean schema changes at run time by the application role, and sequences have no
     row-level security.
   - Every check is the module's own first: the document type is registered, the date's year is 1990
     to 2199, the company is in the caller's scope and their permission's companies, the branch is the
     company's. Row-level security and the company write policies hold behind it.
6. **The register of issued numbers** makes "a number is never given twice per company and document
   type" true whatever an administrator changes later (a reset turned off, a prefix put back), and
   traces each number to its document (CLAUDE.md rule 3's "every posting traces back to the source
   document" starts here). The record's panel lists the last ten.
7. **Changing a sequence** (`PUT /api/numbering/sequences/{id}`, with the version read). A prefix or
   suffix left out keeps the sequence's own (an empty one clears it); digits, reset, gapless and per
   branch are required. `nextValue` is the record's own field of the same name (the value the next
   number of today's period gets): sent back unchanged it changes nothing, so an edit built from the
   record as read is valid. The next value of today's period may be raised (a company moving from another system starts at 1,500),
   never set at or below a number already given, and on a gapless sequence not moved once a number of
   the period was given. When the sequence starts again at another time (yearly to never, never to
   monthly …), the new period's counter goes on from today's count instead of starting at 1, so no
   number of today repeats.
8. **Document types** are registered by modules (`services.AddNumberedDocumentType(...)`, contracts),
   with a name in English and Arabic (server strings) and defaults. Until sales, purchasing,
   inventory, payments and accounting exist, the numbering module registers the eleven a UAE trading
   or manufacturing company numbers (`StandardDocumentTypes`): quotation, sales order, tax invoice, tax
   credit note, purchase order, supplier bill, goods received note, delivery note, receipt voucher,
   payment voucher, journal voucher. Each later module takes its own over. The defaults are the
   product's (a short prefix with the year, five digits, a new run each year, journal vouchers each
   month); tax invoices, tax credit notes and journal vouchers are gapless by default because it is
   the stricter choice. Whether the law requires it is a statutory question (human gate, below).
9. **Screens.** Document numbering (G then N; Settings) is the shared list over every company's
   sequences, the working company's first, each with the number its next document dated today would
   get (searchable in English and Arabic; filters on reset, gapless, per branch, company; printable).
   Opening a sequence puts the cursor in its prefix with the whole prefix chosen (type, Tab, digits,
   Enter): the panel previews the next three numbers as the pattern is typed, when the numbers next
   start again and with what number, whether a number can be skipped, and for a sequence counted per
   branch a branch to preview in; below the form, its counters by period and branch and the last
   numbers given. A user who cannot change it sees it read-only with the reason. The command palette
   finds sequences by type, company, prefix or number.
10. **Demo data.** Al Noor's first company has purchase orders counted to 147 this year, tax invoices
   to 1,202, quotations and journal vouchers part way, delivery notes counted per branch
   (`DN-{BRANCH}-{YY}-`), goods received notes with a suffix; its other companies have invoices
   started. The gate workspaces differ from each other (tenant B's own prefixes, a sequence that never
   starts again, one counted per branch) so the list gates have answers that tell them apart; every
   gate sequence shows the year and the month (a `/{MM}` or `/{YY}{MM}` suffix where its pattern had
   none), so each documented reset is a valid edit of each of them, as the HTTP gate sends them; and
   both companies of each gate workspace hold counters and issued numbers (the company gates judge
   every company table's rows of both).

## Not done here

- The audit trail's screens (p07, built beside this round) will need the three tables registered as
  audited records with their field names in both languages, as p08 did for its tables.
- No HTTP endpoint takes a number: only later modules do, through the contract. A "take a test
  number" button would burn real numbers.
- Fiscal-year resets (a year that starts in April) are not offered: `yearly` is the calendar year of
  the document date. The company's fiscal year start is known (`CompanyInfo`), so this is an
  additive option when accounting needs it.

## Human gate

The UAE VAT rules for tax invoice and tax credit note numbers (the Federal Tax Authority's executive
regulation on what a tax invoice holds, "sequential number") are a statutory rule: not taken from
memory, not implemented as a rule. The product only defaults those two document types to gapless,
which an administrator may change. Listed for the owner in the round's human gates.

## Verify run after merging the p08 currency work (fixer 1, round 1)

The integration branch (p08 currency, 28264abe) was merged into the piece; both modules, both sets of
gate lists and the higher of each ratchet minimum were kept. Full verify through the slots, beside two
other builders' verifies: every test passed (dotnet 822, web 622, e2e 128, compare 487, build 6, all
above their minimums). The only failure is the ratchet's processor-time check: 11,240 s (dotnet 8,293,
web 2,392, e2e 511, timing 44) against the 10,500 s maximum, which belongs to the owner and was not
changed. Log: verify-fixer-p10-numbering-r1-f1.log; kept folder .verify-failed/20261028T190141Z-66514.
