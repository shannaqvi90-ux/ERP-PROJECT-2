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
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.partner/read', parts: ['result.phone'] }], writes: ['POST /web/dataset/call_kw/res.partner/web_save'] },
    ours: { reads: [{ read: 'GET /api/tenancy/companies/*', parts: ['phone'] }], writes: ['PUT /api/tenancy/companies/*'] },
  },
  enters: ['phone'],
  // Round 13 (lead decision on critic p06 r7's question; README "A fixed prefix a field shows"): a
  // fixed prefix a product's field shows as its own before the clock need not be typed. Ours' phone
  // field keeps the UAE's country code for every user and record (its code's HOME_PREFIX); the rest
  // of the number must be typed, and the back end must hold the whole international number. The
  // reference's phone field shows no prefix of its own, so none is declared for it.
  prefixes: {
    ours: [{ field: '[data-field="phone"] input', prefix: '+971', source: 'web/src/kernel/forms/phone.ts', constant: 'HOME_PREFIX' }],
  },
  // Round 14 (critic p01 r13, fix c): the writes set-up sends for each product whose driver is built
  // (set-up puts back a phone an earlier run saved); any other set-up write is refused for our product (lib/runner.mjs setUpFixturesFor).
  fixtures: { ours: ['PUT /api/tenancy/companies/*'] },
  moments: [],
  start: 'Signed in, with the contact {contact.name} open on screen.',
  goal: 'Change the contact\'s phone number to {phone} and save.',
  done: 'The form shows the saved record; the back end holds the new phone number.',
  input: { phone: '+971 50 555 0199' },
  data: ['contact.name', 'contact.mobile'],
  notes: 'Clean-up puts the original number back through the back end.',
};
