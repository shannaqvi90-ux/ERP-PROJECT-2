# p05 — List and search framework

- Server-side query engine any module can register an entity with: filters (typed, combined
  with and/or), sort, keyset paging, grouping with counts and totals, quick search across
  chosen columns backed by proper indexes (for example trigram), all through the API.
- Client list component: virtualised rows, keyboard navigation, column chooser, sort and
  filter from headers, saved views and filters per user and shared, quick search as you type,
  selection with bulk actions, open record by keyboard.
- Search must find one record among 100,000 in well under a second server time and in fewer
  steps and keystrokes than Odoo.
- Every list registered with the framework is automatically attacked by G1 and checked by G2.
- Demo volume: at least one 100,000-record list exists to exercise it (users from p03 or a
  platform list; the critic uses the largest).

Compared against Odoo: find one record among 100,000.
