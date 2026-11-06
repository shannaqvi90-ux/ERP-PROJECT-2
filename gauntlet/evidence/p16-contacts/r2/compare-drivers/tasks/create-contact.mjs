// Critic p16-contacts r2: the spec names "create a contact" as a comparison task (gauntlet/pieces/p16-contacts.md).
export default {
  id: 'create-contact',
  title: 'Create a contact',
  named: false,
  piece: 'p16',
  actor: 'admin',
  startAt: 'home',
  saves: true,
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.partner/search_read', parts: ['result.name', 'result.email', 'result.phone'] }], writes: ['POST /web/dataset/call_kw/res.partner/web_save'] },
    ours: { reads: [{ read: 'GET /api/contacts/contacts', parts: ['items.nameEn', 'items.email', 'items.phone'] }], writes: ['POST /api/contacts/contacts'] },
  },
  enters: ['name', 'email', 'phone'],
  fixtures: { ours: ['PUT /api/contacts/contacts/*', 'POST /api/contacts/contacts/*/archive'] },
  moments: ['contact filled in'],
  start: 'Signed in as an administrator, on the screen the product shows right after sign-in.',
  goal: 'Create the contact "{name}" with the e-mail {email} and the phone number {phone}.',
  done: 'The contact is saved; the back end holds it with that e-mail and phone number.',
  input: { name: 'Falcon Steel Trading LLC', email: 'sales@falconsteel.example', phone: '+971 4 555 0142' },
  notes: 'Set-up removes (Odoo) or renames and archives (ours: contacts are never deleted) any earlier copy, so no duplicate warning differs between runs.',
};
