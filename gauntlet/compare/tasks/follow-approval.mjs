export default {
  id: 'follow-approval',
  title: 'Follow an approval',
  named: true,
  actor: 'approver',
  startAt: 'home',
  moments: ['orders waiting for approval'],
  start: 'Signed in as the approver, on the screen the product shows right after sign-in. A buyer has just submitted a document above the approval limit (AED 7,500 against a limit of AED 5,000).',
  goal: 'Find the document that is waiting for your approval and approve it.',
  done: 'The document is approved; the back end shows it moved past the approval step, approved by the approver.',
  notes: 'Odoo Community has no generic approvals app (Approvals is Enterprise). Its nearest Community feature is the purchase order two-step approval, so the Odoo driver approves a purchase order. Our product follows its own approval flow (p13) for an equivalent document.',
};
