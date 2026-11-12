# p15 — Documented API for everything

- OpenAPI document covering every endpoint with summaries, examples, required permission and
  error model; interactive reference in the product.
- API access for integrations: tokens bound to a tenant and a set of permissions, revocable,
  audited.
- A check that every call the screens make goes to a documented endpoint, so anything a user
  can do on screen can be done through the API.
- Webhooks (owner decision 2026-11-12, needs-human #44): a tenant registers HTTPS addresses for
  the events modules publish (a record created, changed, approved, and so on); deliveries are
  signed (HMAC with a per-subscription secret), retried with back-off by the job system, logged with
  their responses, and can be replayed; addresses that reach private, loopback or link-local
  networks are refused (no server-side request forgery); permissioned (G2), audited, and
  tenant-isolated (G1: one tenant's events never go to another's addresses).

Compared against Odoo: complete a screen task through the API (calls and lines needed).
