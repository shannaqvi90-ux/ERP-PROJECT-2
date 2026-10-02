# p02 — Tenants, companies and branches

- Tenant provisioning (platform operator level), tenant settings.
- Companies within a tenant: legal name in English and Arabic, trade licence and tax
  registration number fields (storage only, no tax logic), base currency (AED default),
  address, logo, fiscal year start.
- Branches within a company.
- Which companies and branches a user may work in; an active company and branch switcher in
  the shell; data that belongs to a company is scoped to it.
- Lists and forms use the shared framework once it exists; full API; audit; permissions;
  English and Arabic.
- Volume: demo seed with realistic numbers of companies and branches.
- Gate coverage: every new endpoint and table is covered by G1 and G2 automatically; add
  company-scoping attacks (user of company X in tenant A reaching company Y they lack).

Compared against Odoo: create a company with a branch; switch the active company.
