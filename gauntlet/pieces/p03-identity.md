# p03 — Users, roles and permissions

- Users: create, invite or set password, edit, deactivate, reset password, lockout, sign-in
  history; per-user language and default company.
- Roles: create, copy, edit, delete; a permission matrix grouped by module with search and
  bulk toggles; roles can be assigned per company.
- Effective-permission view for a user ("what can this person do and why").
- Restricted roles work end to end: a user given a read-only or partial role sees and can do
  only what it grants, in the API and on screen.
- Volume: 100,000 demo users in one tenant so the user list is a 100,000-record list.
- Gate coverage: privilege escalation attempts (assign yourself a role, edit your own
  permissions, grant a permission you do not hold) are part of G2.

Compared against Odoo: create a user with a restricted role; find one user among 100,000.
