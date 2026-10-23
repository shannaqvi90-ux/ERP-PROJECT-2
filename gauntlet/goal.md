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
   possible. A tie is a loss. One exception (owner, 2026-10-08, needs-human #11): a count
   metric (steps, keystrokes, clicks, field entries and the like, never time) on which both
   products score exactly 0 is left out of that task's comparison, neither a tie nor a win.
   Every other metric must still be strictly better for ours; any other tie is still a loss;
   if every metric ties, the task is a loss; the verdict names the metrics left out.
   Second exception (owner, 2026-10-17, needs-human #21): where ours ties Odoo on a task and a
   win cannot be reached, the tie counts as a win, but only after a real try to win. A tie is
   accepted (recorded as `tie-accepted`) only when all of these hold: (a) ours is worse on no
   metric of that path; (b) the same task already tied in an earlier judged round of the same
   piece, so the builder got the tie back as feedback and tried to win; (c) the builder's
   decision record names that task, the paths it tried and why ours cannot be made shorter;
   (d) the critic tries ours' paths itself and finds none that wins. Otherwise a tie is still a
   loss. The verdict names every accepted tie. Recorded verdicts are not changed; the rule
   applies from the first workflow launched after 2026-10-17 16:30 UTC.

OWNER DECISION, 2026-10-23 (needs-human #27). The owner's words: "thek hay, i will go with
your recommendation", on the lead's proposal to move the run forward when rounds keep
coming back BLOCKED. The lead's wording of what was approved:
   a. Two kinds of finding. A product fault (a real tenant leak, a real missing permission
      check, a money, ledger or audit error, a failing test, or a product that does not
      build or run from the clean clone) still blocks the round, as bar item 1 says. A gate
      gap (a fault the critic plants in its own clone that the gate suites miss, while the
      real product shows no such fault) no longer blocks the round: it goes to
      `gauntlet/gate-backlog.md` as work for the piece that owns the gate. Gates still only
      get stricter; backlog items are worked, never dropped. One limit: a surface the piece
      added (an endpoint, screen, job, export or file path) that the isolation or permission
      gates do not reach at all still blocks, because gates are written before building.
   b. A piece that gets a WIN is done for now: it gets no more rounds unless a product fault
      is found in it, its gate backlog is worked in a backlog round, or the owner asks. At
      the decision p00, p03, p04 and p06 had each won a round and are done for now.
   c. At most 3 rounds per piece per wave; a piece that has not won by then rests, its state
      recorded, and comes back in a later wave.
   d. p01 (the Odoo rig and comparison harness) is frozen as it stands: no more rounds of
      its own. Measurement weaknesses go to the backlog and are fixed only when one changes
      a real comparison result.
   e. Where Odoo is strictly better on a task and the builder has tried and named the paths,
      and the critic finds no path for ours to win or tie, the critic marks it a known-loss
      candidate. Once the owner signs a task off as a known loss in needs-human, it no
      longer counts against a WIN; until then it is a loss.
   f. Wave 2 starts now: each workflow takes up to two pieces still working and up to two
      new pieces.
   Recorded verdicts are not changed. The decision applies from the first workflow launched
   after serial-10c.

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
