# Needs a human

Items that reached a human gate from `CLAUDE.md`. The run continues with other pieces; nothing
here is self-approved.

| # | Raised | Piece | Gate | What is needed | Status |
|---|---|---|---|---|---|
| 1 | 2026-10-02 | all | Change to `CLAUDE.md` | `CLAUDE.md` still says `[PROJECT NAME]`. The product uses a neutral working name until the owner picks one and edits the file. | Open |
| 2 | 2026-10-02 | p01 | Change under `bar/` | `CLAUDE.md` puts Odoo reference captures and timings in `bar/reference/`, but anything under `bar/` needs owner approval. Captures are kept in `gauntlet/reference/` until the owner approves moving them. | Open |
| 3 | 2026-10-02 | p00-foundation | Licence approval (`CLAUDE.md` rule 6) | Two transitive npm packages use the ISC licence, which is permissive but not on the MIT/Apache-2.0/BSD/PostgreSQL list: picocolors (pulled in by postcss, which Vite runs at build time) and siginfo (inside vitest). Neither ships to the browser or the server. Both are listed with reasons in `tests/Gates/licence-exceptions.txt` and wait for the owner to approve or reject them. | Approved by the owner, 2026-10-03 |
| 4 | 2026-10-03 | p00-foundation | Tax/statutory rule (CLAUDE.md human gate, rule 8) | Money still rounds the base-currency amount half away from zero, an engineering default with no published source. Before VAT or ledger postings build on it, the rounding rule must be taken from current Federal Tax Authority publications, recorded in docs/compliance/ and signed off by the owner's accountant. This is now noted in docs/decisions/p00-foundation-money.md; nothing was implemented. | Open |
