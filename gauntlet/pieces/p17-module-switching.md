# p17 — Modules switched on per tenant

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

Compared against Odoo: turn a module on for a company and reach its first screen (in Odoo
Community, installing an app from Apps), and find out which modules a company has.
