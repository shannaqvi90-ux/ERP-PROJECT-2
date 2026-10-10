export default {
  id: 'edit-and-save',
  title: 'Edit a record and save it',
  named: false,
  piece: 'p06',
  actor: 'admin',
  startAt: 'record',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.partner/read', parts: ['result.phone'] }] },
    ours: { reads: [{ read: 'GET /api/tenancy/companies/*', parts: ['phone'] }] },
  },
  enters: ['phone'],
  moments: [],
  start: 'Signed in, with the contact {contact.name} open on screen.',
  goal: 'Change the contact\'s phone number to {phone} and save.',
  done: 'The form shows the saved record; the back end holds the new phone number.',
  input: { phone: '+971 50 555 0199' },
  data: ['contact.name', 'contact.mobile'],
  notes: 'Clean-up puts the original number back through the back end.',
};
