# p01 — Odoo reference rig and blind comparison harness

The measuring instrument every other critic uses. Odoo is a reference only: never copy its
code, views or text into the product.

## Must exist

- `tools/odoo-reference/`: Docker Compose for Odoo Community `odoo:20.0` with its own
  PostgreSQL, served on port 8069. One idempotent command (`tools/odoo-reference/up.sh`)
  starts it, creates the database, installs the Community apps needed for the named tasks
  (for example Contacts, Discuss, Purchase for its order approval, base import), installs
  Arabic, and loads at least 100,000 records into each main list that has a counterpart in
  our platform (users, contacts/partners, currency rates, chatter or audit messages,
  attachments, scheduled-job runs or whatever is closest). Record counts are verified and
  written to `gauntlet/reference/odoo/volume.json`.
- `gauntlet/compare/`: a Playwright (Node, TypeScript or JavaScript) harness.
  - Task definitions are product-neutral. Each task has one driver per product: `odoo` and
    `ours`. Drivers take the shortest expert path a trained user would take.
  - Instrumentation counts steps (each click, each key chord, each field entry), keystrokes
    (each key pressed), machine seconds from start to verified completion, and modelled human
    seconds using the keystroke-level model (state the operator times used).
  - Screenshots at each key moment for both products with branding hidden (logos, product
    names, favicon, title, signature colours neutralised by injected CSS) so a reviewer
    cannot tell which product is which. File names do not reveal the product; a separate key
    maps them.
  - Output one JSON per task run with the counts and screenshot paths, written to
    `gauntlet/reference/odoo/` for Odoo baselines and to the critic's evidence folder for
    side-by-side runs.
  - One command runs a task on one or both products (`node gauntlet/compare/run.mjs --task <id> --product odoo|ours|both` or equivalent), documented in `gauntlet/compare/README.md`.
- Odoo baselines for the six named tasks: find one record among 100,000; create a user with a
  restricted role; add a custom field and filter by it; switch to Arabic; import 5,000 rows
  (generate the 5,000-row file); follow an approval. Where Odoo Community lacks a feature
  (for example generic approvals, which are an Enterprise app), use its nearest Community
  feature and say so in the task notes.
- The `ours` drivers start as stubs that report "not built yet"; critics of later pieces
  fill them in.
