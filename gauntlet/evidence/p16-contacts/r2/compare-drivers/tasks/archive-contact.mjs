// Critic p16-contacts r2: the spec names "archive and restore a contact" as a comparison task (gauntlet/pieces/p16-contacts.md).
export default {
  id: 'archive-contact',
  title: 'Archive a contact',
  named: false,
  piece: 'p16',
  actor: 'admin',
  startAt: 'record',
  saves: true,
  endState: {
    odoo: { reads: [{ read: 'POST /web/dataset/call_kw/res.partner/read', parts: ['result.active'] }], writes: ['POST /web/dataset/call_kw/res.partner/action_archive'] },
    ours: { reads: [{ read: 'GET /api/contacts/contacts/*', parts: ['isActive'] }], writes: ['POST /api/contacts/contacts/*/archive'] },
  },
  fixtures: { ours: ['POST /api/contacts/contacts/*/archive', 'POST /api/contacts/contacts/*/restore'] },
  moments: [],
  start: 'Signed in, with the active contact {contact.name} open on screen.',
  goal: 'Archive the contact {contact.name} (take it out of use without deleting it).',
  done: 'The back end holds the contact as archived.',
  data: ['contact.name', 'contact.mobile'],
  notes: 'Set-up puts the contact in the start state through the back end; clean-up makes it active again.',
};
