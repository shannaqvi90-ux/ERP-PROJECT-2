# p03 identity: screen findings of round 7

Round 8, after critic p03 round 7's other findings.

- **Role matrix search by column.** A search word naming a column of the matrix ("view", "عرض",
  or its first three letters at least) matches that column's permissions only. "view" plus
  "Select all shown" ticked "Share list views with everyone" because the List views row's name
  holds the word; it now ticks exactly the view permissions. Other words match labels, resources
  and keys as before ("share", "views").
- **Deleting a role from the keyboard.** Delete role moves the focus to the question's Delete
  button (Enter deletes); Escape or Cancel closes the question without closing the record and gives
  the focus back to Delete role. Before, the focus fell to the page.
- **Effective permissions fit the panel.** The "What they can do" tables use a fixed layout with
  wrapping cells, so long reasons ("Company manager (only in ALN-AUH · Al Noor Technical Services
  LLC)") wrap instead of widening the table past the panel. The end-to-end test at 1440 px checks
  it on the seeded accountant, who holds roles in two companies, in English and Arabic.
- **Where a new user works.** The new-user form ticks the company the signed-in user works in now
  under "Works in" (and the companies of any role given in one company) until companies are chosen
  by hand, so a user created from the keyboard does not land on "No company". The working company
  is read from the tenancy module's own endpoint; a failure leaves nothing ticked, as before.
- **Not changed: sign-in throttling behind the default compose deployment.** Every client reaches
  the app through Docker's port publishing, so the app sees the gateway's address and a pause
  throttles every client of that account until an administrator unblocks it. Telling clients apart
  needs a reverse proxy in front of the app and `ERP_KNOWN_PROXIES` naming it, which is a
  deployment choice (a human gate), not one this piece makes.
