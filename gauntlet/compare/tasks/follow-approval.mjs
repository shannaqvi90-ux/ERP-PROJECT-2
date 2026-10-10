export default {
  id: 'follow-approval',
  title: 'Follow an approval',
  named: true,
  actor: 'approver',
  startAt: 'home',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/purchase.order/read', parts: ['result.state'] }] },
  },
  moments: ['orders waiting for approval'],
  start: 'Signed in as the approver, on the screen the product shows right after sign-in. A buyer has just submitted a document above the approval limit (AED 7,500 against a limit of AED 5,000).',
  goal: 'Find the document that is waiting for your approval and approve it.',
  done: 'The document is approved; the back end shows it moved past the approval step, approved by the approver.',
  notes: 'Odoo Community has no generic approvals app (Approvals is Enterprise). Its nearest Community feature is the purchase order two-step approval, so the Odoo driver approves a purchase order. Our product follows its own approval flow (p13) for an equivalent document.',
};
