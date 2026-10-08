# p03 identity: correcting accounts, names in Arabic, private sign-in history

Round 2, after critic p03 round 1.

## Decision

- **Delete only who never signed in.** `DELETE /api/identity/users/{id}` (`identity.users.delete`)
  removes a user who has never signed in, has no session and no sign-in attempt: the typical case
  is an invitation sent to a mistyped address. Anyone else answers 409
  `identity.userHasSignedIn` and is deactivated instead, because their record is what audit rows,
  sessions and (later) documents point at, and sign-in history is append-only for the application
  role (it may not delete it). The deletion itself is audited (the row trigger records the
  delete with the record's last values), and the takeover rule applies (never oneself, never
  someone stronger).
- **Correct the sign-in address.** `PUT /api/identity/users/{id}` takes an optional `email`. It is
  validated, normalised and unique within the workspace (409 `identity.emailTaken`). Nobody
  changes their own sign-in address through administration (403), so an administrator cannot lock
  themselves out by a typo and a stolen session cannot move an account's sign-in.
- **A name in Arabic script.** `displayNameAr` (optional, 200 characters) on create and edit; the
  list filters it ("contains", on its own GIN trigram index; not a free-text search field, which
  would add a third column to every word's OR and double the search time over 100,000 users) and
  Arabic screens show
  it in place of the Latin name. Seeding fills it from the shared dataset's `name_ar` and gives
  the demo people Arabic names. Not sending the field leaves it alone; an empty value clears it.
- **Sign-in history is not part of "read-only".** The seeded Read-only role no longer includes
  `identity.signIns.read`: colleagues' addresses and browsers are personal data a reader of the
  business records does not need. Administrators keep it. How long sign-in attempts are kept is a
  statutory question (UAE PDPL) and is listed as a human gate, not decided here.

## Screens

The user panel gets the e-mail (disabled on oneself), the Arabic name, and Delete user (with a
confirmation) only when allowed; the sign-in history scrolls inside the panel instead of
overflowing it, paused-until lines isolate the address and the date (`<bdi>`), matrix headers wrap
instead of clipping, and the status cell keeps "Inactive" and "Invited" whole at the list edge.

The session's user carries `displayNameAr` too (an optional, additive field of `SessionUser`), so
the shell greets, labels the user menu, the status bar and printed documents with the Arabic name
on Arabic screens (`sessionUserName` in `web/src/kernel/session.tsx`), and My account shows both
names.

## Round 4: Arabic names are searchable (2026-10-05)

Critic p03 round 3: searching a colleague's name as written in Arabic found nobody. The list
definition gains `ArabicSearchFields` (additive, kernel): a search word written in Arabic letters
matches those fields instead of the search fields. The users list names `displayName` and
`displayNameAr` there; an Arabic word never matches an e-mail address, so every word still ORs two
fields and the search over 100,000 users stays inside the same budget
(`UsersListVolumeTests.One_user_is_found_among_100000_by_the_name_written_in_Arabic_in_well_under_a_second`).
Mixed queries ("romaithi الرميثي") work word by word.
