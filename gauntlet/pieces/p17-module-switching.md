# p17 — Modules, plans and the tenant lifecycle

Added by the owner on 2026-11-12 (needs-human #43), so that a customer can take only the modules
it needs (for example only HR and payroll), and so that every business module after the platform
core is built switchable from its first day.

- Each module declares itself in its registration: an id, its English and Arabic name, whether it
  is part of the platform core (always on) or switchable, and which modules it needs.
- Per tenant, a switchable module is on or off. The platform operator turns modules on and off
  (an operator command beside `./erp tenant ...`, and an operator screen if cheap); a tenant's
  administrator sees which modules the tenant has. Turning one on also requires the modules it
  needs; turning off one that another switched-on module needs is refused with a clear message in
  English and Arabic. Every change is in the audit trail.
- A module that is off for a tenant is gone for that tenant everywhere: no menu entry, screen,
  route, command-palette entry or search result; its API endpoints refuse (one documented status,
  the same as for an unknown route, so nothing about the module leaks); its background jobs do not
  run for that tenant; its permissions cannot be granted in roles. Turning a module off never
  deletes data; turning it on again brings the data back as it was.
- One tenant's switches never change what another tenant sees or can do.
- Gates first: the G1 and G2 gates attack every endpoint of a switchable module with the module off
  for the asking tenant and on for the other; a planted endpoint, menu entry or job that ignores the
  switch is caught, with self-tests.
- The platform core pieces (p00 to p16) stay always on. Until business modules arrive, one small
  demo module shows the mechanism end to end; the demo tenant has it on, a second demo tenant has
  it off.

- Plans and limits (owner decision 2026-11-12, needs-human #44, for selling the product): each
  tenant has a plan that sets its switchable modules and its limits (at least the number of active
  users and the file storage it may use). Reaching a limit is refused on the server with a clear
  message in English and Arabic (no partial writes), and the tenant's administrator sees its use
  against its limits. Billing (taking payment) is out of scope: it needs a paid service, a human
  gate.
- Operator console: a screen for the platform operator (separate from every tenant, its own
  permission, audited) listing tenants with their plan, modules, use and state, to create, suspend,
  reactivate and close a tenant and change its plan; the operator commands do the same.
- Closing a tenant: closing stops every sign-in and job at once, keeps the data for a retention
  period, offers the tenant's full export (p14) during it, and only then deletes the tenant's data
  everywhere (database rows, files, search entries), with a record of the deletion that holds no
  tenant data. The retention period is a statutory question (UAE tax records and data protection
  law): implement it as a setting, list the default as a human gate in needs-human, and never pick
  the period from memory (CLAUDE.md rule 8).

- Self-hosted installs (owner decision 2026-11-13, needs-human #46): besides the cloud, the
  product can later be installed on a customer's own server from released container images (no
  source code shipped). For that, a tenant's plan (its modules and limits) can also come from a
  licence file signed with the publisher's private key and checked offline with the public key
  built into the product: who it is for, the plan, the modules, the limits and an end date. A
  missing, altered, expired or foreign licence is refused with a clear message in English and
  Arabic, and an expired one leaves the data readable and exportable but stops new writes after a
  grace period set in the file. The signing key never enters the repository (a test key signs
  the demo and the tests). The same plan checks apply whether the plan comes from the operator
  console or from a licence file.

Compared against Odoo: turn a module on for a company and reach its first screen (in Odoo
Community, installing an app from Apps), and find out which modules a company has; see a tenant's use against its plan.
