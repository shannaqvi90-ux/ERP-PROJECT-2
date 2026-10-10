export default {
  id: 'configure-sequence',
  title: 'Configure a document sequence',
  named: false,
  piece: 'p10',
  actor: 'admin',
  startAt: 'home',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/ir.sequence/read', parts: ['result.prefix', 'result.padding'] }] },
  },
  enters: ['prefix'],
  moments: ['sequence edited'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Make new purchase orders numbered with the prefix "{prefix}" and {digits} digits (for example {example}).',
  done: 'The purchase order sequence is saved with that prefix and size; the back end confirms the next number would read like {example}.',
  input: { prefix: 'PO-2026-', digits: 6, example: 'PO-2026-000123' },
  notes: 'Set-up puts the sequence back to its default (prefix P, 5 digits); clean-up does the same. Odoo shows its sequences only in developer mode, which the path switches on.',
};
