# The goal and the bar (owner's words, verbatim)

Builders and critics both read this file. Critics judge against it, never against a builder's claims.

---

Build the platform core of this ERP: everything every later module will stand on, and no
business modules yet. Tenants, companies and branches, users, roles and permissions, audit
trail, multi-currency, the English/Arabic app shell, the list / form / search / report
framework all modules will reuse, tenant-defined custom fields, document numbering,
attachments, approval flows, background jobs, import and export, and a documented API for
everything the screens can do. One command must take a clean clone to a running, seeded demo.

THE BAR
1. Hard gates. Write these as automated checks before building. From then on they may only
   get stricter.
   - Tenant isolation: an attacker signed in as tenant A tries every route, query, export,
     job and file path to reach tenant B. Any leak fails the whole round.
   - Permissions: every action is denied unless a role grants it.
   - A clean clone builds, migrates, seeds and passes all tests with one command.
2. Odoo, side by side. Run Odoo Community in Docker on this machine as the reference, loaded
   with the same volume of data (at least 100,000 records in each main list). For every
   piece a user can touch, a critic performs the same task in both products: find one record
   among 100,000, create a user with a restricted role, add a custom field and filter by it,
   switch to Arabic, import 5,000 rows, follow an approval. It compares screenshots, number
   of steps, keystrokes and seconds. Hide the branding so the comparison is blind wherever
   possible. A tie is a loss.

HOW TO WORK
You are the lead. You plan, delegate and keep the record. You do not build and you do not
judge.
Split the goal into the smallest pieces that can be built and judged on their own. You
decide the pieces and what can run in parallel.
Every piece gets a builder subagent and a separate critic subagent with fresh context. The
critic receives the goal, the bar, CLAUDE.md and the running product. It never receives the
builder's reasoning, summary or claims. It runs the real thing: the tests, the app in a
browser, the database. It names the single biggest gap and sends the piece back. Every
retry is judged by a new critic.
After each wave, one fresh agent checks that the whole product still hangs together and
fixes inconsistencies between pieces.
Keep gauntlet/progress.html current (per piece: round number, latest verdict, screenshots,
test counts, timings against Odoo) and append every verdict with its evidence to
gauntlet/ledger.md. Commit after every pass.

WHEN TO STOP
Do not stop at "good" and do not choose a number of rounds. Keep looping until I stop the
run. When a piece reaches a human gate from CLAUDE.md, add it to gauntlet/needs-human.md
and continue with the rest.
