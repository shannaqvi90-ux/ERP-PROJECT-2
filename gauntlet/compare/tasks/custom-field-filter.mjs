export default {
  id: 'custom-field-filter',
  title: 'Add a custom field and filter by it',
  named: true,
  actor: 'admin',
  startAt: 'home',
  saves: true,
  // Where the saved end state lives in each product (round 10): the back-end reads verify() takes it
  // from and the parts of their answers that hold it (a change anywhere else proves nothing), and the
  // writes that save it (the measured part must send one of them).
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/properties.base.definition/read', parts: ['result.properties_definition'] }, { read: 'POST /web/dataset/call_kw/res.partner/read', parts: ['result.properties'] }], writes: ['POST /web/dataset/call_kw/res.partner/web_save'] },
  },
  enters: ['label', 'value'],
  moments: ['field added and filled in'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Add a text field "Licence ref" to contacts, record "LR-7731" in it on the contact {contact.name}, then filter the contact list to the contacts whose Licence ref is LR-7731.',
  done: 'The contact list shows exactly one contact, {contact.name}, filtered by the new field.',
  input: { label: 'Licence ref', technical: 'licence_ref', value: 'LR-7731' },
  data: ['contact.name'],
  notes: 'Set-up removes any earlier copy of the field.',
};
