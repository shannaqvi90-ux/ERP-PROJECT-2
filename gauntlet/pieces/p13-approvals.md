# p13 — Approval flows

- Approval rules per record type: conditions (for example amount over a threshold), steps in
  sequence or in parallel, approver by role or named user, delegation while away.
- Submit, approve, reject with comment, recall; an approval inbox; notifications; the record
  is locked while pending; full audit.
- Contract so later modules make any record type approvable.
- A demo record type to exercise it until business modules arrive.
- Notifications (owner decision 2026-11-12, needs-human #44): a shared notification service any
  module uses through a public contract: in-app notifications (a bell with unread count, by
  keyboard, English and Arabic) and e-mail behind an interface (a local mail catcher in the demo
  and in ./erp verify; a real mail service is a paid service and a human gate). Approvals use it;
  so do user invitations and password-reset e-mails (identity wired through the contract, without
  editing its internals beyond a registration). Users choose which notifications they receive.
  Messages are in the recipient's language. Tenant-isolated (G1) and permissioned (G2).

Compared against Odoo: follow an approval from submission to final approval (Odoo Community's
nearest feature, since its generic Approvals app is Enterprise only).
