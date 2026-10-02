# Needs a human

Items that reached a human gate from `CLAUDE.md`. The run continues with other pieces; nothing
here is self-approved.

| # | Raised | Piece | Gate | What is needed | Status |
|---|---|---|---|---|---|
| 1 | 2026-10-02 | all | Change to `CLAUDE.md` | `CLAUDE.md` still says `[PROJECT NAME]`. The product uses a neutral working name until the owner picks one and edits the file. | Open |
| 2 | 2026-10-02 | p01 | Change under `bar/` | `CLAUDE.md` puts Odoo reference captures and timings in `bar/reference/`, but anything under `bar/` needs owner approval. Captures are kept in `gauntlet/reference/` until the owner approves moving them. | Open |
| 3 | 2026-10-02 | p00-foundation | Licence approval (`CLAUDE.md` rule 6) | Two transitive npm packages use the ISC licence, which is permissive but not on the MIT/Apache-2.0/BSD/PostgreSQL list: picocolors (pulled in by postcss, which Vite runs at build time) and siginfo (inside vitest). Neither ships to the browser or the server. Both are listed with reasons in `tests/Gates/licence-exceptions.txt` and wait for the owner to approve or reject them. | Open |
