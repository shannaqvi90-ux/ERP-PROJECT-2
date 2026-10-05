# p02 — Tenant provisioning is an operator command, not an HTTP surface

Date: 2026-10-03. Piece: p02-tenancy. Status: accepted.

## Decision

- Platform operators run `Erp.Host tenant create|suspend|activate|list` (`./erp tenant …` runs it
  in the setup container). Modules can now add such verbs (`ModuleBuilder.Command`); the platform
  verbs `bootstrap|migrate|seed|setup` are reserved.
- `create` runs the seed runner with the new `Provision` profile for one new tenant. It writes as
  the application role, bound to the new tenant, so row-level security and the audit trail apply.
  The seeders create the tenant row, one company named like the workspace with an `HQ` branch,
  the Administrator role and the first administrator, who gets access to that company. The
  initial password comes from `ERP_TENANT_ADMIN_PASSWORD`, or is generated and printed once. A
  duplicate code is reported without creating anything.
- `suspend`/`activate` change the workspace status. A suspended workspace's sessions stop at
  the next request (the session resolver requires an active tenant) and its users cannot sign in.
- Looking a workspace up by code across tenants (and `list`) needs the database administrator
  connection (`ConnectionStrings:Admin`). The running web app never has it.

## Why

A cross-tenant HTTP endpoint would need a second authentication realm, its own permission model
and its own isolation gate, and it would be the most attractive target in the product. The bar
asks for tenants to be provisioned, not for a web console. A command on the operator's side of
the deployment has no attack surface for tenant users, and still goes through RLS and audit for
everything it writes. A web operator console can come later behind its own gate.
